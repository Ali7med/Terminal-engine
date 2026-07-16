using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Parsing;
using Terminal.Servers.Ssh;

namespace Terminal.Servers.Scan;

/// <summary>
/// يجمع لقطة أداء للخادم (Load/RAM/Uptime + أعلى العمليّات) في نداء SSH واحد مقسَّم بعلامات،
/// تفادياً لعدّة جولات شبكة. المحلّلات في <see cref="OutputParsers"/> نقيّة ومُختبَرة.
/// </summary>
public sealed class PerfMonitor
{
    private const string MarkFree = "===FREE===";
    private const string MarkUp = "===UP===";
    private const string MarkPs = "===PS===";

    private readonly ISshConnection _ssh;

    public PerfMonitor(ISshConnection ssh)
        => _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));

    /// <summary>الأمر المجمّع الذي يلتقط كلّ مقاييس اللقطة دفعةً واحدة.</summary>
    public static string SnapshotCommand(int topProcesses = 10) =>
        "cat /proc/loadavg; " +
        $"echo {MarkFree}; free -k; " +
        $"echo {MarkUp}; (uptime -p 2>/dev/null || uptime); " +
        $"echo {MarkPs}; ps -eo pid,user,pcpu,pmem,comm --sort=-pcpu 2>/dev/null | head -n {topProcesses + 1}";

    /// <summary>ينفّذ اللقطة عن بُعد ويحلّلها.</summary>
    public async Task<PerfSnapshot> SnapshotAsync(int topProcesses = 10, CancellationToken ct = default)
    {
        var r = await _ssh.RunAsync(SnapshotCommand(topProcesses), ct).ConfigureAwait(false);
        return ParseSnapshot(r.StdOut);
    }

    /// <summary>يحلّل مخرجات <see cref="SnapshotCommand"/> إلى <see cref="PerfSnapshot"/> (نقيّ — قابل للاختبار).</summary>
    public static PerfSnapshot ParseSnapshot(string? combined)
    {
        var (load, free, up, ps) = Split(combined ?? string.Empty);
        var (l1, l5, l15) = OutputParsers.ParseLoadAvg(load);
        // نستعمل التفصيل الكامل ونأخذ «المستخدَم الحقيقيّ» (الإجماليّ − المتاح) بدل عمود used الخام،
        // كي تتطابق نسبة الذاكرة في لوحة القيادة مع بطاقة تبويب الإدارة (مصدر واحد للحقيقة).
        var mem = OutputParsers.ParseMemory(free);
        var procs = OutputParsers.ParsePs(ps);
        string uptime = up.Trim();
        return new PerfSnapshot(l1, l5, l15, mem.TotalKb, mem.RealUsedKb, mem.FreeKb, uptime, procs);
    }

    private static (string Load, string Free, string Up, string Ps) Split(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        int iFree = text.IndexOf(MarkFree, StringComparison.Ordinal);
        int iUp = text.IndexOf(MarkUp, StringComparison.Ordinal);
        int iPs = text.IndexOf(MarkPs, StringComparison.Ordinal);

        // إن غابت العلامات (خرج غير متوقّع) نُعيد الكلّ في القسم الأوّل.
        if (iFree < 0 || iUp < 0 || iPs < 0) return (text, string.Empty, string.Empty, string.Empty);

        string load = text.Substring(0, iFree);
        string free = text.Substring(iFree + MarkFree.Length, iUp - (iFree + MarkFree.Length));
        string up = text.Substring(iUp + MarkUp.Length, iPs - (iUp + MarkUp.Length));
        string ps = text.Substring(iPs + MarkPs.Length);
        return (load, free, up, ps);
    }
}
