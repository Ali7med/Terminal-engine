using System;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Terminal.Core.Pty;
using Terminal.Servers.Models;

namespace Terminal.Servers.Ssh;

/// <summary>
/// جلسة شِل تفاعليّة عبر SSH.NET تُنفّذ <see cref="IPtySession"/> — بديل ConPTY لِـ«شِل الحاوية».
/// تفتح اتّصالاً مُصادَقاً ببيانات الاعتماد المخزَّنة (كلمة مرور/مفتاح) ثمّ تخصّص PTY عبر
/// <c>CreateShellStream</c> وتشغّل الأمر البعيد (مثل <c>docker exec -it …</c>) داخله — فلا يُطلب
/// كلمة مرور SSH ثانيةً (على عكس تشغيل <c>ssh.exe</c> محلّيّاً). الاتّصال والقراءة على خيط خلفيّ كي
/// لا يُحجب خيط الواجهة.
/// </summary>
public sealed class SshShellSession : IPtySession
{
    private readonly SshConnectionInfo _info;
    private readonly string _remoteCommand;
    private readonly int _connectTimeoutSeconds;

    private SshClient? _client;
    private ShellStream? _stream;
    private Thread? _thread;
    private volatile bool _disposed;
    private int _exitedRaised;
    private short _cols = 80, _rows = 25;

    public SshShellSession(SshConnectionInfo info, string remoteCommand, int connectTimeoutSeconds = 15)
    {
        _info = info ?? throw new ArgumentNullException(nameof(info));
        _remoteCommand = remoteCommand ?? throw new ArgumentNullException(nameof(remoteCommand));
        _connectTimeoutSeconds = connectTimeoutSeconds;
    }

    public int ProcessId => 0;   // لا عمليّة محلّيّة — جلسة بعيدة
    public int ExitCode { get; private set; }
    public bool HasExited => Volatile.Read(ref _exitedRaised) == 1;

    public event Action<byte[]>? DataReceived;
    public event Action<int>? Exited;

    /// <summary>يبدأ الاتّصال + PTY على خيط خلفيّ (يتجاهل <paramref name="commandLine"/>: الأمر البعيد ثابت).</summary>
    public void Start(string commandLine, string workingDirectory, short columns, short rows)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SshShellSession));
        _cols = columns > 0 ? columns : (short)80;
        _rows = rows > 0 ? rows : (short)25;
        _thread = new Thread(Run) { IsBackground = true, Name = "SSH-Shell" };
        _thread.Start();
    }

    private void Run()
    {
        try
        {
            var ci = ConnectionInfoFactory.Build(_info, TimeSpan.FromSeconds(_connectTimeoutSeconds));
            var client = new SshClient(ci);
            client.Connect();
            _client = client;

            var stream = client.CreateShellStream("xterm-256color", (uint)_cols, (uint)_rows, 0, 0, 4096);
            _stream = stream;

            // ننفّذ الأمر البعيد بديلاً عن شِل الدخول (exec) كي يُغلَق القناة فور انتهائه.
            stream.Write("exec " + _remoteCommand + "\n");
            stream.Flush();

            var buffer = new byte[4096];
            while (!_disposed)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                DataReceived?.Invoke(chunk);
            }
        }
        catch (ObjectDisposedException) { /* أُغلق أثناء الإنهاء — متوقّع */ }
        catch (Exception ex)
        {
            if (!_disposed) EmitLine("\r\n[SSH] " + ex.Message + "\r\n");
        }
        finally
        {
            if (Interlocked.Exchange(ref _exitedRaised, 1) == 0 && !_disposed)
                Exited?.Invoke(ExitCode);
        }
    }

    /// <summary>يُظهر سطراً نصّيّاً في العارض (لرسائل الخطأ عند فشل الاتّصال).</summary>
    private void EmitLine(string text) => DataReceived?.Invoke(Encoding.UTF8.GetBytes(text));

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        try
        {
            var stream = _stream;
            if (stream is not null)
            {
                stream.Write(data.ToArray(), 0, data.Length);
                stream.Flush();
            }
        }
        catch (ObjectDisposedException) { /* أُغلقت الجلسة */ }
        catch (Exception) { /* أفضل جهد — لا نُفشِل الواجهة */ }
        return ValueTask.CompletedTask;
    }

    public void Write(string text) =>
        WriteAsync(Encoding.UTF8.GetBytes(text)).AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// يغيّر حجم الـPTY البعيد فعليّاً (كان لا-عمليّة: يخزّن المقاس ولا يبلّغ الطرف الآخر أبداً،
    /// فيبقى نموذج التطبيق البعيد على المقاس الأوّل بعد أيّ تحجيم ⇒ التفافٌ خاطئ وبقايا أحرف
    /// لا تزول إلّا بإعادة الاتّصال).
    ///
    /// SSH.NET لا يكشف <c>SendWindowChangeRequest</c> على <see cref="ShellStream"/> علنيّاً، فنصل
    /// إلى قناته عبر الانعكاس. أيّ فشل (تغيّر بنية المكتبة) يعود بصمت للسلوك القديم — تخزين المقاس
    /// فقط — فلا نُفشل الجلسة من أجل تحسين.
    /// </summary>
    public void Resize(short columns, short rows)
    {
        if (columns > 0) _cols = columns;
        if (rows > 0) _rows = rows;

        var stream = _stream;
        if (stream is null) return;

        try
        {
            var channel = ChannelField?.GetValue(stream);
            if (channel is not null)
                WindowChangeMethod?.Invoke(channel, new object[] { (uint)_cols, (uint)_rows, 0u, 0u });
        }
        catch { /* أفضل جهد — المقاس مخزَّن على الأقلّ */ }
    }

    /// <summary>حقل القناة الخاصّ داخل <see cref="ShellStream"/> (يُحلَّل مرّة ويُخزَّن).</summary>
    private static readonly FieldInfo? ChannelField =
        typeof(ShellStream).GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// تُحلَّل من <b>نوع الحقل</b> (واجهة <c>IChannelSession</c>) لا من النوع الملموس: لو نُفِّذت
    /// الواجهة تنفيذاً صريحاً لَما ظهرت الدالّة على النوع الملموس، بينما الإرسال عبر الواجهة مضمون.
    /// </summary>
    private static readonly MethodInfo? WindowChangeMethod =
        ChannelField?.FieldType.GetMethod("SendWindowChangeRequest",
            BindingFlags.Public | BindingFlags.Instance);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);

        // قطع الاتّصال بالشبكة والانتظار قد يستغرقان ثوانٍ — نُجريهما على خيط خلفيّ كي لا نحجب
        // خيط الواجهة (كان يُجمّد النوافذ الأخرى عند إغلاق المستكشف).
        var stream = _stream; var client = _client; var thread = _thread;
        _stream = null; _client = null;
        Task.Run(() =>
        {
            try { stream?.Dispose(); } catch { }
            try { if (client?.IsConnected == true) client.Disconnect(); } catch { }
            try { client?.Dispose(); } catch { }
            try { thread?.Join(TimeSpan.FromMilliseconds(500)); } catch { }
        });
    }
}
