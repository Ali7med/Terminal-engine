using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Parsing;
using Terminal.Servers.Ssh;

namespace Terminal.Servers.Scan;

/// <summary>
/// أدوات إدارة الخادم عبر SSH: العمليّات (قائمة + إنهاء)، خدمات systemd (قائمة + تشغيل/إيقاف/إعادة)،
/// والمنافذ المُنصِتة. أوامر الإجراءات مبنيّة بشكل قابل للاختبار (<see cref="BuildKill"/>/<see cref="BuildServiceAction"/>).
/// </summary>
public sealed class ServerAdmin
{
    private static readonly HashSet<string> AllowedServiceActions = new(StringComparer.Ordinal)
    { "start", "stop", "restart" };

    private readonly ISshConnection _ssh;

    public ServerAdmin(ISshConnection ssh)
        => _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));

    public static string BuildKill(int pid, bool force)
        => $"kill {(force ? "-9 " : "")}{pid.ToString(CultureInfo.InvariantCulture)}";

    public static string BuildServiceAction(string action, string name)
    {
        if (!AllowedServiceActions.Contains(action))
            throw new ArgumentException($"إجراء خدمة غير مسموح: {action}", nameof(action));
        return $"systemctl {action} {StorageScanner.ShellQuote(name)}";
    }

    /// <summary>أعلى العمليّات استهلاكاً للمعالج (بالأمر الكامل).</summary>
    public async Task<IReadOnlyList<ProcessInfo>> ListProcessesAsync(int limit = 40, CancellationToken ct = default)
    {
        if (limit < 1) limit = 1;
        string cmd = $"ps -eo pid,user,pcpu,pmem,args --sort=-pcpu 2>/dev/null | head -n {limit + 1}";
        var r = await _ssh.RunAsync(cmd, ct).ConfigureAwait(false);
        return OutputParsers.ParsePs(r.StdOut);
    }

    /// <summary>أعلى العمليّات استهلاكاً للذاكرة (مرتّبة بـ RSS تنازليّاً) — تشمل الخاملة عالية الذاكرة.</summary>
    public async Task<IReadOnlyList<ProcMemInfo>> ListProcessesByMemoryAsync(int limit = 40, CancellationToken ct = default)
    {
        if (limit < 1) limit = 1;
        string cmd = $"ps -eo pid,user,rss,pmem,args --sort=-rss 2>/dev/null | head -n {limit + 1}";
        var r = await _ssh.RunAsync(cmd, ct).ConfigureAwait(false);
        return OutputParsers.ParsePsMem(r.StdOut);
    }

    /// <summary>تفصيل الذاكرة (Mem + Swap) من <c>free -k</c>.</summary>
    public async Task<MemoryInfo> MemoryAsync(CancellationToken ct = default)
    {
        var r = await _ssh.RunAsync("free -k 2>/dev/null", ct).ConfigureAwait(false);
        return OutputParsers.ParseMemory(r.StdOut);
    }

    /// <summary>ينهي عمليّة (<c>kill</c> أو <c>kill -9</c>). يرمي إن فشل.</summary>
    public async Task KillAsync(int pid, bool force, CancellationToken ct = default)
    {
        var r = await _ssh.RunAsync(BuildKill(pid, force) + " 2>&1", ct).ConfigureAwait(false);
        if (!r.Ok) throw new InvalidOperationException(Detail(r) ?? $"تعذّر إنهاء العمليّة {pid}.");
    }

    /// <summary>قائمة خدمات systemd (كلّها).</summary>
    public async Task<IReadOnlyList<ServiceInfo>> ListServicesAsync(CancellationToken ct = default)
    {
        var r = await _ssh.RunAsync(
            "systemctl list-units --type=service --all --no-legend --no-pager --plain 2>/dev/null", ct).ConfigureAwait(false);
        return OutputParsers.ParseServices(r.StdOut);
    }

    /// <summary>تشغيل/إيقاف/إعادة تشغيل خدمة. يرمي إن فشل (مثل غياب الصلاحيّة).</summary>
    public async Task ServiceActionAsync(string name, string action, CancellationToken ct = default)
    {
        var r = await _ssh.RunAsync(BuildServiceAction(action, name) + " 2>&1", ct).ConfigureAwait(false);
        if (!r.Ok) throw new InvalidOperationException(Detail(r) ?? $"تعذّر تنفيذ {action} على {name}.");
    }

    /// <summary>المنافذ المُنصِتة (TCP).</summary>
    public async Task<IReadOnlyList<PortInfo>> ListPortsAsync(CancellationToken ct = default)
    {
        var r = await _ssh.RunAsync("ss -tlnH 2>/dev/null || ss -tln 2>/dev/null", ct).ConfigureAwait(false);
        return OutputParsers.ParsePorts(r.StdOut);
    }

    private static string? Detail(CommandResult r)
    {
        string s = (r.StdOut + "\n" + r.StdError).Trim();
        return s.Length == 0 ? null : s;
    }
}
