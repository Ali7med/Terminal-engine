using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Parsing;
using Terminal.Servers.Ssh;

namespace Terminal.Servers.Scan;

/// <summary>
/// يحلّل تخزين الخادم عبر أوامر SSH قياسيّة (df/du/find) ويحوّلها إلى نماذج عبر
/// <see cref="OutputParsers"/>. لا يحمل حالة UI — يأخذ <see cref="ISshConnection"/> جاهزاً.
/// </summary>
public sealed class StorageScanner
{
    private readonly ISshConnection _ssh;

    public StorageScanner(ISshConnection ssh)
        => _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));

    /// <summary>يقتبس مساراً لصدفة POSIX (لفّ بعلامتَي اقتباس مفردتَين مع تهريب الداخليّة).</summary>
    public static string ShellQuote(string value)
        => "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";

    /// <summary>فحص سريع: أقراص/نقاط التركيب وأحجامها (<c>df -kP</c>).</summary>
    public async Task<IReadOnlyList<DiskInfo>> QuickScanDisksAsync(CancellationToken ct = default)
    {
        var r = await _ssh.RunAsync("df -kP", ct).ConfigureAwait(false);
        return OutputParsers.ParseDf(r.StdOut);
    }

    /// <summary>
    /// أحجام المجلّدات المباشرة داخل مسار (<c>du -kx --max-depth=1</c>). العَلَم <c>-x</c> يُبقي الفحص
    /// على نظام الملفّات نفسه فلا يعبر إلى الأنظمة الوهميّة (/proc,/sys,/dev) أو الأقراص المركّبة الأخرى —
    /// تسريعٌ كبير وسلوكٌ مطابق لعرض القرص في df.
    /// </summary>
    public async Task<IReadOnlyList<DirEntry>> ScanFolderAsync(string path, CancellationToken ct = default)
    {
        string cmd = $"du -kx --max-depth=1 {ShellQuote(path)} 2>/dev/null";
        var r = await _ssh.RunAsync(cmd, ct).ConfigureAwait(false);
        return OutputParsers.ParseDu(r.StdOut);
    }

    /// <summary>
    /// يفصل نتيجة <see cref="ScanFolderAsync"/> إلى إجماليّ المجلّد نفسه + مجلّداته الفرعيّة المباشرة
    /// (مرتّبة تنازليّاً بالحجم). <c>du --max-depth=1</c> يُدرج سطر الجذر نفسه، فنُزيله من الأبناء.
    /// </summary>
    public async Task<FolderScan> ScanSubfoldersAsync(string path, CancellationToken ct = default)
    {
        var all = await ScanFolderAsync(path, ct).ConfigureAwait(false);
        string root = Normalize(path);
        long total = 0;
        var children = new List<DirEntry>();
        foreach (var d in all)
        {
            if (Normalize(d.Path) == root) { total = d.SizeBytes; continue; }
            children.Add(d);
        }
        return new FolderScan(root, total, children);
    }

    private static string Normalize(string path)
    {
        string p = (path ?? string.Empty).TrimEnd('/');
        return p.Length == 0 ? "/" : p;
    }

    /// <summary>
    /// الملفّات المباشرة داخل مجلّد (غير متكرّر — <c>-maxdepth 1</c>)، مرتّبة تنازليّاً بالحجم.
    /// للوحة تفاصيل المجلّد في مستكشف المجلّدات.
    /// </summary>
    public async Task<IReadOnlyList<FileEntry>> ListFilesAsync(string path, CancellationToken ct = default)
    {
        string cmd =
            $"find {ShellQuote(path)} -maxdepth 1 -type f -printf '%s\\t%TY-%Tm-%TdT%TH:%TM\\t%p\\n' 2>/dev/null " +
            $"| sort -rn";
        var r = await _ssh.RunAsync(cmd, ct).ConfigureAwait(false);
        return OutputParsers.ParseFindFiles(r.StdOut);
    }

    /// <summary>أكبر <paramref name="count"/> ملفّاً تحت مسار (find + sort + head).</summary>
    public async Task<IReadOnlyList<FileEntry>> LargestFilesAsync(string path, int count = 100, CancellationToken ct = default)
    {
        if (count < 1) count = 1;
        string cmd =
            $"find {ShellQuote(path)} -xdev -type f -printf '%s\\t%TY-%Tm-%TdT%TH:%TM\\t%p\\n' 2>/dev/null " +
            $"| sort -rn | head -n {count}";
        var r = await _ssh.RunAsync(cmd, ct).ConfigureAwait(false);
        return OutputParsers.ParseFindFiles(r.StdOut);
    }
}
