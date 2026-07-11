using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Ssh;

namespace Terminal.Servers.Scan;

/// <summary>
/// يجمع معلومات النظام العامّة (المضيف/التوزيعة/النواة/المعالج/الـ IP) في نداء SSH واحد مقسَّم
/// بعلامات. المحلّل <see cref="Parse"/> نقيّ ومُختبَر.
/// </summary>
public sealed class SystemInfoScanner
{
    private const string MOs = "===OS===";
    private const string MKernel = "===KERNEL===";
    private const string MCpu = "===CPU===";
    private const string MCores = "===CORES===";
    private const string MIp = "===IP===";

    private readonly ISshConnection _ssh;

    public SystemInfoScanner(ISshConnection ssh)
        => _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));

    /// <summary>الأمر المجمّع الذي يلتقط كلّ حقول معلومات النظام دفعةً واحدة.</summary>
    public static string SnapshotCommand() =>
        "hostname; " +
        $"echo {MOs}; (. /etc/os-release 2>/dev/null; echo \"$PRETTY_NAME\"); " +
        $"echo {MKernel}; uname -r; " +
        $"echo {MCpu}; (grep -m1 'model name' /proc/cpuinfo 2>/dev/null | cut -d: -f2-); " +
        $"echo {MCores}; nproc 2>/dev/null; " +
        $"echo {MIp}; (hostname -I 2>/dev/null | awk '{{print $1}}')";

    public async Task<SystemInfo> SnapshotAsync(CancellationToken ct = default)
    {
        var r = await _ssh.RunAsync(SnapshotCommand(), ct).ConfigureAwait(false);
        return Parse(r.StdOut);
    }

    /// <summary>يحلّل مخرجات <see cref="SnapshotCommand"/> إلى <see cref="SystemInfo"/> (نقيّ — قابل للاختبار).</summary>
    public static SystemInfo Parse(string? combined)
    {
        string text = (combined ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        string host = Section(text, null, MOs);
        string os = Section(text, MOs, MKernel);
        string kernel = Section(text, MKernel, MCpu);
        string cpu = Section(text, MCpu, MCores);
        string coresRaw = Section(text, MCores, MIp);
        string ip = Section(text, MIp, null);

        int cores = int.TryParse(coresRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) ? c : 0;
        return new SystemInfo(host.Trim(), os.Trim(), kernel.Trim(), cpu.Trim(), cores, ip.Trim());
    }

    /// <summary>يقتطع النصّ بين علامتَين (أو من البداية/إلى النهاية إن كانت إحداهما null).</summary>
    private static string Section(string text, string? from, string? to)
    {
        int start = 0;
        if (from != null)
        {
            int i = text.IndexOf(from, StringComparison.Ordinal);
            if (i < 0) return string.Empty;
            start = i + from.Length;
        }
        int end = text.Length;
        if (to != null)
        {
            int j = text.IndexOf(to, start, StringComparison.Ordinal);
            if (j >= 0) end = j;
        }
        return text.Substring(start, end - start).Trim('\n', ' ', '\t');
    }
}
