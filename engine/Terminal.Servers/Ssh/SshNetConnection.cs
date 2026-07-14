using System;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;
using Terminal.Servers.Models;

namespace Terminal.Servers.Ssh;

/// <summary>
/// تنفيذ <see cref="ISshConnection"/> فوق مكتبة SSH.NET (Renci). يدعم المصادقة بكلمة مرور
/// أو مفتاح خاصّ (PEM). أوامر SSH.NET متزامنة، فنغلّفها بـ <see cref="Task.Run(Action)"/> كي لا
/// نحجب خيط الواجهة. آمن للتخلّص (Dispose يغلق العميل).
///
/// إعادة اتّصال تلقائيّة: الجلسات تسقط بعد خمول طويل (مهلة الخادم/الشبكة). قبل كل أمر نتحقّق من
/// الاتّصال ونعيد فتحه إن سقط، وإن سقط أثناء التنفيذ نعيد الاتّصال مرّة ونعيد المحاولة — فالطبقات
/// الأعلى (الماسح/المراقب) لا تلحظ الانقطاع. <c>KeepAliveInterval</c> يقلّل السقوط أصلاً.
/// </summary>
public sealed class SshNetConnection : ISshConnection
{
    private readonly SshConnectionInfo _info;
    private readonly object _gate = new();
    private SshClient? _client;

    /// <summary>مهلة الاتّصال (ثوانٍ).</summary>
    public int ConnectTimeoutSeconds { get; init; } = 15;

    /// <summary>فترة إبقاء الجلسة حيّة (ثوانٍ) — تمنع إسقاط الخادم للجلسة الخاملة.</summary>
    public int KeepAliveSeconds { get; init; } = 30;

    /// <summary>يُستدعى قبل كل محاولة إعادة اتّصال تلقائيّة (لعرض توست في الواجهة). قد يُستدعى من خيط غير خيط الواجهة.</summary>
    public Action? OnReconnecting { get; set; }

    public SshNetConnection(SshConnectionInfo info)
        => _info = info ?? throw new ArgumentNullException(nameof(info));

    public bool IsConnected => _client?.IsConnected == true;

    public Task ConnectAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        lock (_gate) OpenLocked(ct, notify: false);
    }, ct);

    public Task<CommandResult> RunAsync(string command, CancellationToken ct = default) => Task.Run(() =>
    {
        EnsureConnected(ct);
        try
        {
            return Execute(command, ct);
        }
        catch (Exception ex) when (IsConnectionDrop(ex))
        {
            // الاتّصال سقط أثناء التنفيذ — أعد الاتّصال مرّة واحدة ثم أعد المحاولة.
            EnsureConnected(ct, forceReopen: true);
            return Execute(command, ct);
        }
    }, ct);

    /// <summary>
    /// ينفّذ أمراً ويبثّ مخرجاته الخام (stdout) إلى <paramref name="dest"/> — آمن للبيانات الثنائيّة
    /// (تنزيل ملفّات). يعيد رمز الخروج. لا يعيد المحاولة عند السقوط (تنزيل لمرّة واحدة).
    /// </summary>
    public Task<int> DownloadToStreamAsync(string command, System.IO.Stream dest, CancellationToken ct = default)
        => Task.Run(() =>
        {
            EnsureConnected(ct);
            var client = _client ?? throw new InvalidOperationException("SSH غير متّصل.");
            using var cmd = client.CreateCommand(command);
            var async = cmd.BeginExecute();
            using (var os = cmd.OutputStream)
                os.CopyTo(dest);
            cmd.EndExecute(async);
            return cmd.ExitStatus ?? -1;
        }, ct);

    /// <summary>يضمن جلسة مفتوحة قبل التنفيذ (يعيد الفتح إن سقطت). آمن للاستدعاء المتزامن.</summary>
    private void EnsureConnected(CancellationToken ct, bool forceReopen = false)
    {
        if (!forceReopen && IsConnected) return;
        lock (_gate)
        {
            if (!forceReopen && IsConnected) return;   // فحص مزدوج: ربّما أعاد خيطٌ آخر الاتّصال
            OpenLocked(ct, notify: true);
        }
    }

    /// <summary>يبني عميلاً جديداً ويتّصل. يجب استدعاؤه داخل <see cref="_gate"/>.</summary>
    private void OpenLocked(CancellationToken ct, bool notify)
    {
        ct.ThrowIfCancellationRequested();
        if (IsConnected) return;

        if (notify)
        {
            try { OnReconnecting?.Invoke(); } catch { /* لا تُفشل الاتّصال بسبب الواجهة */ }
        }

        try { _client?.Dispose(); } catch { /* تجاهل */ }
        _client = null;

        var connectionInfo = ConnectionInfoFactory.Build(_info, TimeSpan.FromSeconds(ConnectTimeoutSeconds));
        var client = new SshClient(connectionInfo);
        if (KeepAliveSeconds > 0) client.KeepAliveInterval = TimeSpan.FromSeconds(KeepAliveSeconds);
        client.Connect();
        _client = client;
    }

    private CommandResult Execute(string command, CancellationToken ct)
    {
        var client = _client ?? throw new InvalidOperationException("SSH غير متّصل.");
        ct.ThrowIfCancellationRequested();
        using var cmd = client.CreateCommand(command);
        string stdout = cmd.Execute();
        return new CommandResult(cmd.ExitStatus ?? -1, stdout ?? string.Empty, cmd.Error ?? string.Empty);
    }

    /// <summary>هل الاستثناء يدلّ على سقوط الاتّصال (لا خطأ مصادقة/منطق)؟ عندها نعيد الاتّصال ونعيد المحاولة.</summary>
    private static bool IsConnectionDrop(Exception ex)
        => ex is SshConnectionException
              or SshOperationTimeoutException
              or System.Net.Sockets.SocketException
              or ObjectDisposedException
        || (ex is SshException && ex is not SshAuthenticationException);

    public void Disconnect()
    {
        lock (_gate)
        {
            try { if (_client?.IsConnected == true) _client.Disconnect(); }
            catch { /* تجاهل أخطاء الإغلاق */ }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { if (_client?.IsConnected == true) _client.Disconnect(); } catch { }
            _client?.Dispose();
            _client = null;
        }
    }
}
