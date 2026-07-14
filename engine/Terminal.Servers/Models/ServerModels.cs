namespace Terminal.Servers.Models;

/// <summary>طريقة المصادقة على الخادم.</summary>
public enum SshAuthKind
{
    /// <summary>كلمة مرور.</summary>
    Password,
    /// <summary>مفتاح خاصّ (PEM/OpenSSH).</summary>
    PrivateKey,
}

/// <summary>
/// بيانات اتّصال SSH كاملة (تتضمّن السرّ الخام) — تُبنى لحظةَ الاتّصال فقط ولا تُخزَّن هكذا على القرص.
/// السرّ يُحفَظ مُعمّى (DPAPI) في طبقة التخزين ويُفكّ عند بناء هذا الكائن.
/// </summary>
public sealed record SshConnectionInfo(
    string Host,
    int Port,
    string Username,
    SshAuthKind AuthKind,
    string? Password = null,
    string? PrivateKeyPem = null,
    string? PrivateKeyPassphrase = null);

/// <summary>نتيجة تنفيذ أمر عن بُعد عبر SSH.</summary>
public sealed record CommandResult(int ExitCode, string StdOut, string StdError)
{
    /// <summary>هل نجح الأمر (خرج بالرمز 0)؟</summary>
    public bool Ok => ExitCode == 0;
}

/// <summary>قرص/نقطة تركيب من مخرجات <c>df</c> (بالبايت).</summary>
public sealed record DiskInfo(
    string Filesystem,
    string MountPoint,
    long TotalBytes,
    long UsedBytes,
    long AvailBytes,
    double UsePercent);

/// <summary>مجلّد وحجمه من مخرجات <c>du</c> (بالبايت).</summary>
public sealed record DirEntry(string Path, long SizeBytes);

/// <summary>نتيجة فحص مجلّد: مساره وإجماليّ حجمه + مجلّداته الفرعيّة المباشرة (تنازليّاً).</summary>
public sealed record FolderScan(
    string Path,
    long TotalBytes,
    System.Collections.Generic.IReadOnlyList<DirEntry> Children);

/// <summary>ملفّ وحجمه وتاريخ تعديله من مخرجات <c>find</c> (بالبايت).</summary>
public sealed record FileEntry(string Path, string Name, long SizeBytes, System.DateTimeOffset? Modified);

/// <summary>موقع الملفّ داخل بنية Docker على القرص.</summary>
public enum DockerPathKind
{
    /// <summary>ليس داخل طبقة overlay2 ولا حجم Docker.</summary>
    None,
    /// <summary>طبقة تخزين overlay2 (<c>/var/lib/docker/overlay2/&lt;id&gt;/…</c>).</summary>
    Overlay,
    /// <summary>حجم مُسمّى (<c>/var/lib/docker/volumes/&lt;name&gt;/_data/…</c>).</summary>
    Volume,
}

/// <summary>حاوية Docker مطابِقة لطبقة/حجم — من مخرجات <c>docker inspect</c>.</summary>
public sealed record DockerContainerMatch(
    string Name,
    string Status,
    string Image,
    string CoolifyName,
    string ComposeProject,
    /// <summary>true إن كان الملفّ في طبقة الكتابة لهذه الحاوية (مالك حصريّ)؛ false لحجم مُركَّب.</summary>
    bool WritableLayer);

/// <summary>نتيجة «اعرف الحاوية»: نوع الموقع + المعرّف المستخرَج + الحاويات المطابِقة.</summary>
public sealed record DockerLookup(
    DockerPathKind Kind,
    string Key,
    System.Collections.Generic.IReadOnlyList<DockerContainerMatch> Matches);

/// <summary>حاوية من قائمة <c>docker ps -a</c> (كلّ الحاويات على الخادم).</summary>
public sealed record ContainerListItem(string Id, string Name, string State, string Status, string Image)
{
    /// <summary>هل الحاوية تعمل الآن (<c>state == running</c>)؟</summary>
    public bool Running => string.Equals(State, "running", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>معرّف قصير (12 محرفاً) — مستقرّ وآمن كهدف <c>docker exec</c>.</summary>
    public string ShortId => Id.Length > 12 ? Id.Substring(0, 12) : Id;
}

/// <summary>مدخل في مستكشف ملفّات الحاوية (اسم + هل هو مجلّد) من <c>ls -1Ap</c>.</summary>
public sealed record ContainerEntry(string Name, bool IsDir);

/// <summary>عمليّة قيد التشغيل من مخرجات <c>ps</c>/<c>top</c>.</summary>
public sealed record ProcessInfo(int Pid, string User, double CpuPercent, double MemPercent, string Command);

/// <summary>خدمة systemd من مخرجات <c>systemctl list-units</c>.</summary>
public sealed record ServiceInfo(string Name, string Load, string Active, string Sub, string Description)
{
    /// <summary>هل الخدمة تعمل الآن (active/running)؟</summary>
    public bool IsActive => string.Equals(Active, "active", System.StringComparison.OrdinalIgnoreCase);
}

/// <summary>منفذ مُنصِت من مخرجات <c>ss -tln</c> (البروتوكول + المنفذ + العنوان + العمليّة).</summary>
public sealed record PortInfo(string Protocol, int Port, string Address, string Process);

/// <summary>معلومات النظام العامّة (اسم المضيف، التوزيعة، النواة، المعالج، الـ IP) — للوحة القيادة.</summary>
public sealed record SystemInfo(
    string Hostname,
    string OsName,
    string Kernel,
    string CpuModel,
    int CpuCores,
    string Ip);

/// <summary>لقطة أداء لحظيّة للخادم (CPU/RAM/Load/Uptime + أعلى العمليّات).</summary>
public sealed record PerfSnapshot(
    double LoadAvg1,
    double LoadAvg5,
    double LoadAvg15,
    long MemTotalKb,
    long MemUsedKb,
    long MemFreeKb,
    string Uptime,
    System.Collections.Generic.IReadOnlyList<ProcessInfo> TopProcesses)
{
    /// <summary>نسبة استخدام الذاكرة (0..100)، أو 0 إن كان الإجماليّ غير معروف.</summary>
    public double MemUsedPercent => MemTotalKb > 0 ? System.Math.Round(MemUsedKb * 100.0 / MemTotalKb, 1) : 0;
}
