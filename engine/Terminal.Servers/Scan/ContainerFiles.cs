using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Ssh;

namespace Terminal.Servers.Scan;

/// <summary>
/// يسرد محتوى مجلّد <b>داخل</b> حاوية Docker عبر <c>docker exec &lt;id&gt; ls -lA -- &lt;path&gt;</c>
/// (تنسيق طويل: صلاحيّات/حجم/تاريخ/اسم، بلا <c>. ..</c>) — مدعوم في busybox (صور alpine) وGNU سواءً،
/// فلا نعتمد على <c>find -printf</c> غير المتوفّر في busybox. المسار يُمرَّر كوسيط لـ <c>ls</c> مباشرةً
/// (بلا صدفة داخل الحاوية) فلا يُفسَّر، و<c>--</c> يوقف تحليل الخيارات.
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

    /// <summary>
    /// أمر سرد مجلّد داخل الحاوية بالتنسيق الطويل. نطلب أوّلاً <c>--full-time</c> ليعطي التاريخ بصيغة ISO
    /// (<c>YYYY-MM-DD HH:MM:SS</c>) — مدعوم في GNU وأغلب صور busybox — فإن عجزت الصدفة (busybox قديم لا يعرف
    /// الخيار) نرجع للتنسيق الافتراضيّ. المسار مُقتبَس للصدفة المضيفة فقط (طبقة اقتباس واحدة).
    ///
    /// المخرجات تُلتقَط في متغيّر ثمّ تُطبَع عند النجاح فقط: لو طبع <c>ls</c> السردَ ثمّ خرج برمز غير صفريّ
    /// (GNU يخرج 1 على «مشاكل طفيفة» مثل مدخل يتعذّر فحصه) لا يختلط سرده مع سرد الاحتياط فتتكرّر الصفوف.
    /// </summary>
    public static string BuildList(string containerId, string path, bool sudo = false)
    {
        string pfx = DockerCli.Prefix(sudo) + "docker exec " + containerId + " ls -lA";
        string tail = " -- " + StorageScanner.ShellQuote(NormalizePath(path));
        return "o=$(" + pfx + " --full-time" + tail + " 2>/dev/null) && printf '%s\\n' \"$o\" || "
             + pfx + tail + " 2>&1";
    }

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

    // أنواع المداخل المعروفة في أوّل حرف من سطر ls -l (المتبقّي = "total N" أو سطر غير صالح فيُتخطّى).
    private const string EntryTypes = "d-lbcsp";

    /// <summary>
    /// يحلّل مخرجات <c>ls -lA</c> (سواء <c>--full-time</c> بصيغة ISO أو التنسيق الافتراضيّ):
    /// يستخرج النوع (أوّل حرف) والحجم (الحقل الخامس) ووقت التعديل والاسم (بقيّة السطر — يحافظ على المسافات).
    /// التاريخ يُطبَّع إلى <c>YYYY-MM-DD HH:MM</c> عند توفّر صيغة ISO (قابل للفرز)، وإلّا يُترك خاماً
    /// مع مفتاح فرز زمنيّ مستقلّ (<see cref="ContainerEntry.ModifiedSort"/>).
    /// روابط <c>l</c> يُزال منها <c>-&gt; target</c>. مرتّبة: المجلّدات أوّلاً ثمّ أبجديّاً.
    /// </summary>
    public static IReadOnlyList<ContainerEntry> Parse(string stdout)
    {
        var dirs = new List<ContainerEntry>();
        var files = new List<ContainerEntry>();
        foreach (var raw in (stdout ?? string.Empty).Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length < 10 || EntryTypes.IndexOf(line[0]) < 0) continue;   // يتخطّى "total N" وغيره

            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 8) continue;

            char type = line[0];
            long size = long.TryParse(f[4], out var sz) ? sz : 0;

            // التاريخ: صيغة ISO من --full-time (f[5]=YYYY-MM-DD ، f[6]=HH:MM:SS[.ns] ، وقد يليه منطقة TZ)
            // أو الافتراضيّ (f[5]=Mon ، f[6]=Day ، f[7]=Time/Year). نحدّد موضع بدء الاسم تبعاً لذلك.
            string modified, sortKey; int nameField;
            if (IsIsoDate(f[5]))
            {
                string time = f[6].Length >= 5 ? f[6][..5] : f[6];   // HH:MM (نقصّ الثواني/الكسور)
                modified = sortKey = f[5] + " " + time;
                // حقل TZ يضيفه GNU مع كسور الثانية (full-iso). نشترط الكسر كي لا يُخطئ اسمَ ملفّ
                // يبدأ بنمط ‎±HHMM‎ على صيغة busybox (التي لا تحوي TZ أصلاً) فيُبتَر الاسم.
                nameField = f.Length > 7 && f[6].Contains('.') && IsTimezone(f[7]) ? 8 : 7;
            }
            else
            {
                if (f.Length < 9) continue;
                modified = f[5] + " " + f[6] + " " + f[7];
                sortKey = LegacySortKey(f[5], f[6], f[7]);
                nameField = 8;
            }

            string name = AfterFields(line, nameField);   // بقيّة السطر = الاسم (يحفظ المسافات)
            if (type == 'l')
            {
                int arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow >= 0) name = name[..arrow];
            }
            name = name.Trim();
            if (name.Length == 0 || name is "." or "..") continue;

            bool isDir = type == 'd';
            (isDir ? dirs : files).Add(new ContainerEntry(name, isDir, size, type, modified, sortKey));
        }
        dirs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        dirs.AddRange(files);
        return dirs;
    }

    /// <summary>هل النصّ تاريخ ISO (<c>YYYY-MM-DD</c>)؟ — للتمييز بين مخرجات <c>--full-time</c> والافتراضيّة.</summary>
    private static bool IsIsoDate(string s)
        => s.Length >= 10 && s[4] == '-' && s[7] == '-' && char.IsDigit(s[0]) && char.IsDigit(s[1]);

    private static readonly string[] Months =
        { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

    /// <summary>
    /// مفتاح فرز زمنيّ للتنسيق الافتراضيّ (<c>Mon Day Time|Year</c>) — يحوّله إلى <c>YYYY-MM-DD HH:MM</c>
    /// كي يفرز زمنيّاً لا أبجديّاً بأسماء الأشهر. <c>ls</c> يطبع الوقت للملفّات الحديثة (أقلّ من ٦ أشهر)
    /// والسنة للأقدم؛ فنمنح صيغةَ الوقت سنةً قصوى (9999) لتبقى الأحدث، بلا اعتماد على ساعة النظام.
    /// </summary>
    private static string LegacySortKey(string mon, string day, string timeOrYear)
    {
        int m = System.Array.IndexOf(Months, mon.ToLowerInvariant()) + 1;
        if (m == 0) return "0000-00-00 00:00";   // شهر غير معروف → الأقدم
        bool isTime = timeOrYear.Contains(':');
        string year = isTime ? "9999" : timeOrYear.PadLeft(4, '0');
        string time = isTime ? timeOrYear : "00:00";
        _ = int.TryParse(day, out int d);
        return $"{year}-{m:00}-{d:00} {time}";
    }

    /// <summary>هل النصّ حقل منطقة زمنيّة (<c>+HHMM</c>/<c>-HHMM</c>) كما يضيفه GNU مع <c>--full-time</c>؟</summary>
    private static bool IsTimezone(string s)
    {
        if (s.Length != 5 || (s[0] != '+' && s[0] != '-')) return false;
        for (int i = 1; i < 5; i++) if (!char.IsDigit(s[i])) return false;
        return true;
    }

    /// <summary>يعيد بقيّة السطر بعد أوّل <paramref name="n"/> حقلاً مفصولاً بمسافات (للاسم مع الحفاظ على مسافاته).</summary>
    private static string AfterFields(string line, int n)
    {
        int i = 0, field = 0;
        while (i < line.Length)
        {
            while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;   // تخطّي الحقل
            field++;
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;     // تخطّي المسافات
            if (field == n) return line[i..];
        }
        return "";
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
