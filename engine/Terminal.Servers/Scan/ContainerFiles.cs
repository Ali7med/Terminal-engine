using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Ssh;

namespace Terminal.Servers.Scan;

/// <summary>
/// يسرد محتوى مجلّد <b>داخل</b> حاوية Docker عبر <c>docker exec &lt;id&gt; ls -1Ap -- &lt;path&gt;</c>.
/// نستعمل <c>ls -1Ap</c> (سطر لكلّ مدخل، بلا <c>. ..</c>، ولاحقة <c>/</c> للمجلّدات) — مدعوم في busybox
/// (صور alpine) وGNU سواءً، فلا نعتمد على <c>find -printf</c> غير المتوفّر في busybox. المسار يُمرَّر
/// كوسيط لـ <c>ls</c> مباشرةً (بلا صدفة داخل الحاوية) فلا يُفسَّر، و<c>--</c> يوقف تحليل الخيارات.
/// </summary>
public sealed class ContainerFiles
{
    private readonly ISshConnection _ssh;
    private readonly string _containerId;
    private readonly bool _sudo;

    public ContainerFiles(ISshConnection ssh, string containerId, bool sudo = false)
    {
        _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));
        _containerId = containerId ?? throw new ArgumentNullException(nameof(containerId));
        _sudo = sudo;
    }

    /// <summary>الحدّ الأقصى لعرض محتوى ملفّ (512KB) — يمنع سحب ملفّات ضخمة عبر الشبكة.</summary>
    public const int MaxViewBytes = 512 * 1024;

    /// <summary>أمر سرد مجلّد داخل الحاوية (المسار مُقتبَس للصدفة المضيفة فقط — طبقة اقتباس واحدة).</summary>
    public static string BuildList(string containerId, string path, bool sudo = false)
        => DockerCli.Prefix(sudo) + "docker exec " + containerId +
           " ls -1Ap -- " + StorageScanner.ShellQuote(NormalizePath(path)) + " 2>&1";

    /// <summary>أمر قراءة أوّل <paramref name="maxBytes"/> بايت من ملفّ داخل الحاوية (<c>head -c</c>).</summary>
    public static string BuildRead(string containerId, string path, bool sudo = false, int maxBytes = MaxViewBytes)
        => DockerCli.Prefix(sudo) + "docker exec " + containerId +
           " head -c " + maxBytes + " -- " + StorageScanner.ShellQuote(path) + " 2>&1";

    /// <summary>أمر بثّ كامل محتوى ملفّ خاماً (للتنزيل الثنائيّ الآمن — بلا <c>2>&amp;1</c> كي لا يختلط الخطأ).</summary>
    public static string BuildCat(string containerId, string path, bool sudo = false)
        => DockerCli.Prefix(sudo) + "docker exec " + containerId +
           " cat -- " + StorageScanner.ShellQuote(path);

    // ===== عمليّات كتابة (docker exec يشغّل الأداة مباشرةً — طبقة اقتباس مضيفة واحدة، بلا صدفة داخليّة) =====

    /// <summary>أمر حذف ملفّ (<c>rm -f</c>) أو مجلّد بمحتوياته (<c>rm -rf</c>) داخل الحاوية.</summary>
    public static string BuildDelete(string containerId, string path, bool recursive, bool sudo = false)
        => DockerCli.Prefix(sudo) + "docker exec " + containerId +
           (recursive ? " rm -rf -- " : " rm -f -- ") + StorageScanner.ShellQuote(path) + " 2>&1";

    /// <summary>أمر إنشاء مجلّد (<c>mkdir -p</c>) داخل الحاوية.</summary>
    public static string BuildMkdir(string containerId, string path, bool sudo = false)
        => DockerCli.Prefix(sudo) + "docker exec " + containerId +
           " mkdir -p -- " + StorageScanner.ShellQuote(path) + " 2>&1";

    /// <summary>أمر إعادة تسمية/نقل (<c>mv</c>) داخل الحاوية.</summary>
    public static string BuildRename(string containerId, string from, string to, bool sudo = false)
        => DockerCli.Prefix(sudo) + "docker exec " + containerId +
           " mv -- " + StorageScanner.ShellQuote(from) + " " + StorageScanner.ShellQuote(to) + " 2>&1";

    /// <summary>أمر نسخ ملفّ من المضيف إلى داخل الحاوية (<c>docker cp</c>) — للرفع/الحفظ.</summary>
    public static string BuildCopyIn(string containerId, string hostPath, string containerPath, bool sudo = false)
        => DockerCli.Prefix(sudo) + "docker cp " + StorageScanner.ShellQuote(hostPath) + " " +
           StorageScanner.ShellQuote(containerId + ":" + containerPath) + " 2>&1";

    /// <summary>يحذف ملفّاً/مجلّداً داخل الحاوية. يرمي إن فشل.</summary>
    public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
        => RunOkAsync(BuildDelete(_containerId, path, recursive, _sudo), ct);

    /// <summary>ينشئ مجلّداً داخل الحاوية. يرمي إن فشل.</summary>
    public Task MakeDirectoryAsync(string path, CancellationToken ct = default)
        => RunOkAsync(BuildMkdir(_containerId, path, _sudo), ct);

    /// <summary>يعيد تسمية/نقل داخل الحاوية. يرمي إن فشل.</summary>
    public Task RenameAsync(string from, string to, CancellationToken ct = default)
        => RunOkAsync(BuildRename(_containerId, from, to, _sudo), ct);

    /// <summary>ينسخ ملفّاً من المضيف إلى الحاوية (<c>docker cp</c>). يرمي إن فشل.</summary>
    public Task CopyInAsync(string hostPath, string containerPath, CancellationToken ct = default)
        => RunOkAsync(BuildCopyIn(_containerId, hostPath, containerPath, _sudo), ct);

    /// <summary>ينفّذ أمراً ويرمي إن لم ينجح (يضمّ المخرجات في الرسالة).</summary>
    private async Task RunOkAsync(string command, CancellationToken ct)
    {
        var r = await _ssh.RunAsync(command, ct).ConfigureAwait(false);
        if (!r.Ok)
        {
            string msg = (r.StdOut + " " + r.StdError).Trim();
            throw new InvalidOperationException(msg.Length == 0 ? $"فشل الأمر (رمز {r.ExitCode})." : msg);
        }
    }

    /// <summary>يحلّل مخرجات <c>ls -1Ap</c> إلى مداخل (لاحقة '/' = مجلّد)، مرتّبةً: المجلّدات أوّلاً ثمّ أبجديّاً.</summary>
    public static IReadOnlyList<ContainerEntry> Parse(string stdout)
    {
        var dirs = new List<ContainerEntry>();
        var files = new List<ContainerEntry>();
        foreach (var raw in (stdout ?? string.Empty).Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            bool isDir = line.EndsWith('/');
            string name = isDir ? line[..^1] : line;
            if (name.Length == 0 || name is "." or "..") continue;
            (isDir ? dirs : files).Add(new ContainerEntry(name, isDir));
        }
        dirs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        dirs.AddRange(files);
        return dirs;
    }

    /// <summary>يسرد مجلّداً داخل الحاوية. يرمي إن فشل <c>docker exec</c> (حاوية متوقّفة/مسار غير موجود).</summary>
    public async Task<IReadOnlyList<ContainerEntry>> ListAsync(string path, CancellationToken ct = default)
    {
        var r = await _ssh.RunAsync(BuildList(_containerId, path, _sudo), ct).ConfigureAwait(false);
        if (!r.Ok)
        {
            string msg = (r.StdOut + " " + r.StdError).Trim();
            throw new InvalidOperationException(msg.Length == 0 ? $"تعذّر سرد المسار (رمز {r.ExitCode})." : msg);
        }
        return Parse(r.StdOut);
    }

    /// <summary>
    /// يقرأ محتوى ملفّ (حتى <see cref="MaxViewBytes"/>) داخل الحاوية كنصّ. يرمي إن فشل <c>docker exec</c>
    /// (ملفّ غير موجود/حاوية متوقّفة). المخرجات الخام تُعاد كما هي (قد تكون ثنائيّة — يكشفها المتّصل).
    /// </summary>
    public async Task<string> ReadFileAsync(string path, CancellationToken ct = default)
    {
        var r = await _ssh.RunAsync(BuildRead(_containerId, path, _sudo), ct).ConfigureAwait(false);
        if (!r.Ok)
        {
            string msg = (r.StdOut + " " + r.StdError).Trim();
            throw new InvalidOperationException(msg.Length == 0 ? $"تعذّر قراءة الملفّ (رمز {r.ExitCode})." : msg);
        }
        return r.StdOut;
    }

    /// <summary>هل يبدو النصّ ثنائيّاً (يحوي بايت NUL ضمن أوّل مقطع)؟ للتمييز بين نصّ ومحتوى ثنائيّ.</summary>
    public static bool LooksBinary(string content)
    {
        int scan = System.Math.Min(content.Length, 8000);
        for (int i = 0; i < scan; i++)
            if (content[i] == '\0') return true;
        return false;
    }

    /// <summary>يوحّد المسار: فارغ ⇒ "/"، ويزيل الشرطة الزائدة عدا الجذر.</summary>
    public static string NormalizePath(string? path)
    {
        string p = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        if (p.Length > 1) p = p.TrimEnd('/');
        return p.Length == 0 ? "/" : p;
    }

    /// <summary>يضمّ مقطعاً فرعيّاً لمسار أب (يتعامل مع الجذر ولاحقة الشرطة).</summary>
    public static string Join(string parent, string child)
    {
        string p = NormalizePath(parent);
        return p == "/" ? "/" + child : p + "/" + child;
    }

    /// <summary>المسار الأب (أو "/" إن كان الجذر).</summary>
    public static string Parent(string path)
    {
        string p = NormalizePath(path);
        if (p == "/") return "/";
        int i = p.LastIndexOf('/');
        return i <= 0 ? "/" : p[..i];
    }
}
