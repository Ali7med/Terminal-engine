using System;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Ssh;

namespace Terminal.Servers.Scan;

/// <summary>
/// عمليّات ملفّات عن بُعد عبر أوامر SSH (حذف/إعادة تسمية/تذييل سجلّ). المسارات مقتبسة بأمان
/// عبر <see cref="StorageScanner.ShellQuote"/> و<c>--</c> لإيقاف تحليل الخيارات، فلا تُفسَّر أسماء
/// تبدأ بشرطة أو تحوي محارف صدفة. الأوامر تُبنى بشكل قابل للاختبار عبر <see cref="BuildDelete"/> ونظائرها.
/// </summary>
public sealed class FileOperations
{
    private readonly ISshConnection _ssh;

    public FileOperations(ISshConnection ssh)
        => _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));

    public static string BuildDelete(string path) => $"rm -f -- {StorageScanner.ShellQuote(path)}";
    public static string BuildDeleteFolder(string path) => $"rm -rf -- {StorageScanner.ShellQuote(path)}";
    public static string BuildMkdir(string path) => $"mkdir -p -- {StorageScanner.ShellQuote(path)}";
    public static string BuildRename(string from, string to)
        => $"mv -- {StorageScanner.ShellQuote(from)} {StorageScanner.ShellQuote(to)}";
    public static string BuildTail(string path, int lines)
        => $"tail -n {(lines < 1 ? 1 : lines)} -- {StorageScanner.ShellQuote(path)} 2>/dev/null";

    /// <summary>يحذف ملفّاً (<c>rm -f</c>). يرمي إن فشل الأمر عن بُعد.</summary>
    public async Task DeleteAsync(string path, CancellationToken ct = default)
        => EnsureOk(await _ssh.RunAsync(BuildDelete(path), ct).ConfigureAwait(false));

    /// <summary>يحذف مجلّداً بمحتوياته (<c>rm -rf</c>). خطير — يتطلّب تأكيداً في الواجهة. يرمي إن فشل.</summary>
    public async Task DeleteFolderAsync(string path, CancellationToken ct = default)
        => EnsureOk(await _ssh.RunAsync(BuildDeleteFolder(path), ct).ConfigureAwait(false));

    /// <summary>ينشئ مجلّداً (<c>mkdir -p</c>). يرمي إن فشل.</summary>
    public async Task MakeDirectoryAsync(string path, CancellationToken ct = default)
        => EnsureOk(await _ssh.RunAsync(BuildMkdir(path), ct).ConfigureAwait(false));

    /// <summary>يعيد تسمية/نقل ملفّ (<c>mv</c>). يرمي إن فشل.</summary>
    public async Task RenameAsync(string from, string to, CancellationToken ct = default)
        => EnsureOk(await _ssh.RunAsync(BuildRename(from, to), ct).ConfigureAwait(false));

    /// <summary>يُعيد آخر <paramref name="lines"/> سطراً من ملفّ (<c>tail</c>) — لعارض السجلّات.</summary>
    public async Task<string> TailAsync(string path, int lines = 1000, CancellationToken ct = default)
        => (await _ssh.RunAsync(BuildTail(path, lines), ct).ConfigureAwait(false)).StdOut;

    private static void EnsureOk(CommandResult r)
    {
        if (!r.Ok)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(r.StdError) ? $"فشل الأمر (رمز {r.ExitCode})." : r.StdError.Trim());
    }
}
