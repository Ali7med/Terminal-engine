using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Terminal.Servers.Models;

namespace Terminal.Servers.Parsing;

/// <summary>
/// محلّلات نقيّة لمخرجات أوامر يونكس القياسيّة (df/du/find/free/ps/loadavg). بلا شبكة —
/// تأخذ نصّاً وتُعيد نماذج، فتُختبَر بعيّنات مخرجات حقيقيّة. كلّ الأرقام تُحلّل بـ InvariantCulture.
/// </summary>
public static class OutputParsers
{
    private static readonly char[] Ws = { ' ', '\t' };

    private static IEnumerable<string> Lines(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length > 0) yield return line;
        }
    }

    private static long ParseLong(string s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0L;

    private static double ParseDouble(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0;

    /// <summary>
    /// يحلّل مخرجات <c>df -kP</c> (POSIX، كتل 1024 بايت، صفّ واحد لكلّ نظام ملفّات).
    /// الأعمدة: Filesystem  1024-blocks  Used  Available  Capacity%  Mounted-on.
    /// يتجاهل سطر الترويسة والأنظمة الوهميّة (tmpfs/udev/proc/sys/cgroup...).
    /// </summary>
    public static IReadOnlyList<DiskInfo> ParseDf(string? output)
    {
        var result = new List<DiskInfo>();
        foreach (var line in Lines(output))
        {
            var p = line.Split(Ws, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 6) continue;
            if (string.Equals(p[0], "Filesystem", StringComparison.OrdinalIgnoreCase)) continue;

            // آخر عمود = نقطة التركيب (قد تحوي مسافات فتُدمَج بقيّة الأعمدة).
            string fs = p[0];
            long total = ParseLong(p[1]) * 1024;
            long used = ParseLong(p[2]) * 1024;
            long avail = ParseLong(p[3]) * 1024;
            double pct = ParseDouble(p[4].TrimEnd('%'));
            string mount = string.Join(' ', p, 5, p.Length - 5);

            if (total <= 0) continue; // أنظمة وهميّة بلا حجم حقيقيّ
            result.Add(new DiskInfo(fs, mount, total, used, avail, pct));
        }
        return result;
    }

    /// <summary>
    /// يحلّل مخرجات <c>du -k</c> (كتل 1024 بايت): كلّ سطر «&lt;size&gt;\t&lt;path&gt;».
    /// يُعيد المجلّدات مرتّبةً تنازليّاً بالحجم.
    /// </summary>
    public static IReadOnlyList<DirEntry> ParseDu(string? output)
    {
        var result = new List<DirEntry>();
        foreach (var line in Lines(output))
        {
            int tab = line.IndexOfAny(Ws);
            if (tab <= 0) continue;
            long kb = ParseLong(line.Substring(0, tab));
            string path = line.Substring(tab).Trim();
            if (path.Length == 0) continue;
            result.Add(new DirEntry(path, kb * 1024));
        }
        result.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return result;
    }

    /// <summary>
    /// يحلّل مخرجات <c>find … -printf '%s\t%TY-%Tm-%TdT%TH:%TM\t%p\n'</c>:
    /// «&lt;bytes&gt;\t&lt;yyyy-MM-ddTHH:mm&gt;\t&lt;path&gt;». التاريخ اختياريّ (قد يكون فارغاً).
    /// </summary>
    public static IReadOnlyList<FileEntry> ParseFindFiles(string? output)
    {
        var result = new List<FileEntry>();
        foreach (var line in Lines(output))
        {
            var parts = line.Split('\t');
            if (parts.Length < 3) continue;
            long size = ParseLong(parts[0].Trim());
            DateTimeOffset? modified = null;
            if (DateTimeOffset.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                modified = dt;
            string path = string.Join('\t', parts, 2, parts.Length - 2).Trim();
            if (path.Length == 0) continue;
            string name = path;
            int slash = path.LastIndexOf('/');
            if (slash >= 0 && slash < path.Length - 1) name = path.Substring(slash + 1);
            result.Add(new FileEntry(path, name, size, modified));
        }
        return result;
    }

    /// <summary>يحلّل محتوى <c>/proc/loadavg</c> («0.00 0.01 0.05 1/234 5678»).</summary>
    public static (double L1, double L5, double L15) ParseLoadAvg(string? output)
    {
        foreach (var line in Lines(output))
        {
            var p = line.Split(Ws, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 3)
                return (ParseDouble(p[0]), ParseDouble(p[1]), ParseDouble(p[2]));
        }
        return (0, 0, 0);
    }

    /// <summary>
    /// يحلّل مخرجات <c>free -k</c>: يقرأ سطر «Mem:» (total/used/free بالكيلوبايت).
    /// </summary>
    public static (long TotalKb, long UsedKb, long FreeKb) ParseFree(string? output)
    {
        foreach (var line in Lines(output))
        {
            if (!line.StartsWith("Mem:", StringComparison.OrdinalIgnoreCase)) continue;
            var p = line.Split(Ws, StringSplitOptions.RemoveEmptyEntries);
            // Mem: total used free shared buff/cache available
            if (p.Length >= 4)
                return (ParseLong(p[1]), ParseLong(p[2]), ParseLong(p[3]));
        }
        return (0, 0, 0);
    }

    /// <summary>
    /// يحلّل مخرجات <c>free -k</c> (سطرا «Mem:» و«Swap:») إلى <see cref="MemoryInfo"/>. متسامح مع نقص
    /// عمود «available» (إصدارات/busybox قديمة): يُقدّره حينها بـ <c>free + buff/cache</c>.
    /// </summary>
    public static MemoryInfo ParseMemory(string? output)
    {
        long mTotal = 0, mUsed = 0, mFree = 0, mShared = 0, mBuff = 0, mAvail = 0, sTotal = 0, sUsed = 0;
        foreach (var line in Lines(output))
        {
            if (line.StartsWith("Mem:", StringComparison.OrdinalIgnoreCase))
            {
                var p = line.Split(Ws, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4)
                {
                    mTotal = ParseLong(p[1]); mUsed = ParseLong(p[2]); mFree = ParseLong(p[3]);
                    mShared = p.Length > 4 ? ParseLong(p[4]) : 0;
                    mBuff = p.Length > 5 ? ParseLong(p[5]) : 0;
                    mAvail = p.Length > 6 ? ParseLong(p[6]) : mFree + mBuff;   // احتياط للإصدارات القديمة
                }
            }
            else if (line.StartsWith("Swap:", StringComparison.OrdinalIgnoreCase))
            {
                var p = line.Split(Ws, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 3) { sTotal = ParseLong(p[1]); sUsed = ParseLong(p[2]); }
            }
        }
        return new MemoryInfo(mTotal, mUsed, mFree, mShared, mBuff, mAvail, sTotal, sUsed);
    }

    /// <summary>
    /// يحلّل مخرجات <c>ps -eo pid,user,rss,pmem,comm --sort=-rss</c> (RSS بالكيلوبايت). يتجاهل الترويسة.
    /// </summary>
    public static IReadOnlyList<ProcMemInfo> ParsePsMem(string? output)
    {
        var result = new List<ProcMemInfo>();
        foreach (var line in Lines(output))
        {
            var p = line.Split(Ws, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 5) continue;
            if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)) continue; // ترويسة
            string user = p[1];
            long rss = ParseLong(p[2]);
            double mem = ParseDouble(p[3]);
            string cmd = string.Join(' ', p, 4, p.Length - 4);
            result.Add(new ProcMemInfo(pid, user, rss, mem, cmd));
        }
        return result;
    }

    /// <summary>
    /// يحلّل مخرجات <c>ps -eo pid,user,pcpu,pmem,comm --sort=-pcpu</c> (بلا ترويسة أو معها).
    /// يتجاهل سطر الترويسة (PID/USER…).
    /// </summary>
    public static IReadOnlyList<ProcessInfo> ParsePs(string? output)
    {
        var result = new List<ProcessInfo>();
        foreach (var line in Lines(output))
        {
            var p = line.Split(Ws, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 5) continue;
            if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)) continue; // ترويسة
            string user = p[1];
            double cpu = ParseDouble(p[2]);
            double mem = ParseDouble(p[3]);
            string cmd = string.Join(' ', p, 4, p.Length - 4);
            result.Add(new ProcessInfo(pid, user, cpu, mem, cmd));
        }
        return result;
    }

    /// <summary>
    /// يحلّل مخرجات <c>systemctl list-units --type=service --plain --no-legend</c>:
    /// «UNIT LOAD ACTIVE SUB DESCRIPTION». يتخطّى نقطة الحالة البادئة (●) إن وُجدت.
    /// </summary>
    public static IReadOnlyList<ServiceInfo> ParseServices(string? output)
    {
        var result = new List<ServiceInfo>();
        foreach (var line in Lines(output))
        {
            var p = line.Split(Ws, StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            if (p.Length > 0 && p[0].Length == 1 && !char.IsLetterOrDigit(p[0][0])) i = 1;   // نقطة الحالة ●
            if (p.Length - i < 4) continue;
            string name = p[i];
            if (!name.Contains('.')) continue;   // أسماء الوحدات تحمل .service
            string desc = p.Length - i > 4 ? string.Join(' ', p, i + 4, p.Length - (i + 4)) : "";
            result.Add(new ServiceInfo(name, p[i + 1], p[i + 2], p[i + 3], desc));
        }
        return result;
    }

    /// <summary>
    /// يحلّل مخرجات <c>ss -tlnH</c> (منافذ TCP المُنصِتة بلا ترويسة): يستخرج المنفذ والعنوان واسم العمليّة.
    /// </summary>
    public static IReadOnlyList<PortInfo> ParsePorts(string? output)
    {
        var result = new List<PortInfo>();
        foreach (var line in Lines(output))
        {
            var p = line.Split(Ws, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 4) continue;
            string local = p[3];                       // State Recv-Q Send-Q Local[3] Peer …
            int colon = local.LastIndexOf(':');
            if (colon < 0) continue;
            if (!int.TryParse(local[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)) continue;
            string addr = local[..colon];

            string proc = "";
            foreach (var f in p)
                if (f.StartsWith("users:", StringComparison.Ordinal))
                {
                    var m = Regex.Match(f, "\"([^\"]+)\"");
                    if (m.Success) proc = m.Groups[1].Value;
                    break;
                }
            result.Add(new PortInfo("tcp", port, addr, proc));
        }
        return result;
    }
}
