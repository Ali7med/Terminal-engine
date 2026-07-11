using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Terminal.Servers.Models;

namespace Terminal.Servers.Ssh;

/// <summary>
/// عميل SFTP لبروتوكول SSH (فوق SSH.NET) لتنزيل الملفّات من الخادم. اتّصال مستقلّ عن جلسة exec
/// لكن بنفس بيانات الاعتماد. آمن للتخلّص. عمليّاته متزامنة فنغلّفها بـ <see cref="Task.Run(Action)"/>.
/// </summary>
public sealed class SshNetSftp : IDisposable
{
    private readonly SshConnectionInfo _info;
    private SftpClient? _client;

    public int ConnectTimeoutSeconds { get; init; } = 15;

    public SshNetSftp(SshConnectionInfo info)
        => _info = info ?? throw new ArgumentNullException(nameof(info));

    public Task ConnectAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        var ci = ConnectionInfoFactory.Build(_info, TimeSpan.FromSeconds(ConnectTimeoutSeconds));
        var client = new SftpClient(ci);
        client.Connect();
        _client = client;
    }, ct);

    /// <summary>
    /// ينزّل ملفّاً بعيداً إلى مسار محلّيّ (يُنشئ/يستبدل الملفّ المحلّيّ). <paramref name="progress"/>
    /// اختياريّ يُستدعى بإجماليّ البايتات المنزَّلة حتى الآن (لعرض الشريط/السرعة).
    /// </summary>
    public Task DownloadAsync(string remotePath, string localPath,
        Action<ulong>? progress = null, CancellationToken ct = default) => Task.Run(() =>
    {
        var client = _client ?? throw new InvalidOperationException("SFTP غير متّصل.");
        using var fs = File.Create(localPath);
        client.DownloadFile(remotePath, fs, progress);
    }, ct);

    /// <summary>يرفع ملفّاً محلّيّاً إلى مسار بعيد (يُنشئ/يستبدل). <paramref name="progress"/> يبلّغ البايتات المرفوعة.</summary>
    public Task UploadAsync(string localPath, string remotePath,
        Action<ulong>? progress = null, CancellationToken ct = default) => Task.Run(() =>
    {
        var client = _client ?? throw new InvalidOperationException("SFTP غير متّصل.");
        using var fs = File.OpenRead(localPath);
        client.UploadFile(fs, remotePath, progress);
    }, ct);

    public void Dispose()
    {
        try { if (_client?.IsConnected == true) _client.Disconnect(); }
        catch { /* تجاهل */ }
        _client?.Dispose();
        _client = null;
    }
}
