using System;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Terminal.Servers.Models;

namespace Terminal.Servers.Ssh;

/// <summary>
/// تنفيذ <see cref="ISshConnection"/> فوق مكتبة SSH.NET (Renci). يدعم المصادقة بكلمة مرور
/// أو مفتاح خاصّ (PEM). أوامر SSH.NET متزامنة، فنغلّفها بـ <see cref="Task.Run(Action)"/> كي لا
/// نحجب خيط الواجهة. آمن للتخلّص (Dispose يغلق العميل).
/// </summary>
public sealed class SshNetConnection : ISshConnection
{
    private readonly SshConnectionInfo _info;
    private SshClient? _client;

    /// <summary>مهلة الاتّصال (ثوانٍ).</summary>
    public int ConnectTimeoutSeconds { get; init; } = 15;

    public SshNetConnection(SshConnectionInfo info)
        => _info = info ?? throw new ArgumentNullException(nameof(info));

    public bool IsConnected => _client?.IsConnected == true;

    public Task ConnectAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        var connectionInfo = ConnectionInfoFactory.Build(_info, TimeSpan.FromSeconds(ConnectTimeoutSeconds));
        var client = new SshClient(connectionInfo);
        client.Connect();
        _client = client;
    }, ct);

    public Task<CommandResult> RunAsync(string command, CancellationToken ct = default) => Task.Run(() =>
    {
        var client = _client ?? throw new InvalidOperationException("SSH غير متّصل.");
        using var cmd = client.CreateCommand(command);
        string stdout = cmd.Execute();
        return new CommandResult(cmd.ExitStatus ?? -1, stdout ?? string.Empty, cmd.Error ?? string.Empty);
    }, ct);

    public void Disconnect()
    {
        try { if (_client?.IsConnected == true) _client.Disconnect(); }
        catch { /* تجاهل أخطاء الإغلاق */ }
    }

    public void Dispose()
    {
        Disconnect();
        _client?.Dispose();
        _client = null;
    }
}
