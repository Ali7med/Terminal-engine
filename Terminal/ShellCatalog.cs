using System.Collections.Generic;
using System.Linq;
using TerminalLauncher.Models;
using TerminalLauncher.Services;

namespace TerminalLauncher.Terminal;

/// <summary>
/// تعريف صدفة قابل للعرض في الكومبو: مفتاح، اسم معروض، سطر تشغيل ConPTY، نهاية السطر،
/// وإتاحة. يحمل أيضاً مجلّد العمل ومتغيّرات البيئة كي تستهلكها الجلسة دون بحثٍ إضافيّ.
/// </summary>
public sealed record ShellDef(
    string Key,
    string Display,
    string CommandLine,
    string Newline,
    bool Available,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null)
{
    // يمنع ظهور الـ record كاملاً في الـ ComboBox (يظهر الاسم فقط).
    public override string ToString() => Display;
}

/// <summary>
/// كتالوج الصدفات: يدمج الصدفات المكتشَفة تلقائياً (CMD/PowerShell/pwsh/Git Bash/WSL)
/// مع البروفايلات المخصّصة المحفوظة (T-101). يُهيَّأ مرّة عبر <see cref="Initialize"/>؛
/// ويبقى <see cref="All"/>/<see cref="Get"/> متوافقين مع النداءات القائمة.
/// </summary>
public static class ShellCatalog
{
    private static readonly object Gate = new();
    private static List<ShellProfile> _profiles = new();
    private static IReadOnlyList<ShellDef> _defs = FallbackDefs();
    private static string? _defaultProfileId;

    /// <summary>كل الصدفات المتاحة (مكتشَفة + مخصّصة) كتعريفات عرض.</summary>
    public static IReadOnlyList<ShellDef> All
    {
        get { lock (Gate) return _defs; }
    }

    /// <summary>كل البروفايلات (مكتشَفة + مخصّصة) — للطبقات التي تحتاج التفاصيل الكاملة.</summary>
    public static IReadOnlyList<ShellProfile> Profiles
    {
        get { lock (Gate) return _profiles; }
    }

    /// <summary>معرّف البروفايل الافتراضيّ الحاليّ (أوّل بروفايل متاح إن لم يُضبَط).</summary>
    public static string DefaultKey
    {
        get
        {
            lock (Gate)
                return _defaultProfileId ?? _profiles.FirstOrDefault(p => p.Available)?.Id ?? "cmd";
        }
    }

    /// <summary>
    /// يكتشف الصدفات ويدمج البروفايلات المخصّصة المحفوظة، ثم يبني تعريفات العرض.
    /// يُستدعى مرّة عند الإقلاع (وبعد أيّ تعديل على البروفايلات المخصّصة).
    /// </summary>
    public static void Initialize(IEnumerable<ShellProfile>? customProfiles, string? defaultProfileId)
    {
        var merged = new List<ShellProfile>();
        merged.AddRange(ShellDetector.DetectAll());
        if (customProfiles != null)
            foreach (var p in customProfiles) { p.IsBuiltIn = false; p.Available = true; merged.Add(p); }

        lock (Gate)
        {
            _profiles = merged;
            _defaultProfileId = defaultProfileId;
            _defs = merged
                .Where(p => p.Available)
                .Select(ToDef)
                .ToList();
        }
    }

    /// <summary>يحوّل بروفايلاً إلى تعريف عرض (سطر التشغيل + بيئة + مجلد عمل).</summary>
    private static ShellDef ToDef(ShellProfile p) => new(
        p.Id,
        p.DisplayLabel,
        p.BuildCommandLine(),
        p.Newline,
        p.Available,
        string.IsNullOrWhiteSpace(p.WorkingDirectory) ? null : p.WorkingDirectory,
        p.EnvironmentVariables.Count > 0 ? new Dictionary<string, string>(p.EnvironmentVariables) : null);

    /// <summary>تعريف الصدفة بمفتاحها (أو الافتراضيّ إن لم يوجد).</summary>
    public static ShellDef Get(string? key)
    {
        lock (Gate)
        {
            foreach (var s in _defs)
                if (s.Key == key) return s;
            // إن لم يُطابَق المفتاح: الافتراضيّ ثمّ أوّل متاح.
            foreach (var s in _defs)
                if (s.Key == DefaultKey) return s;
            return _defs.Count > 0 ? _defs[0] : FallbackDefs()[0];
        }
    }

    /// <summary>البروفايل الكامل بمعرّفه (أو null).</summary>
    public static ShellProfile? GetProfile(string? id)
    {
        lock (Gate)
            return _profiles.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>تعريفات احتياطيّة (cmd فقط) قبل التهيئة — يضمن ألّا يكون الكتالوج فارغاً أبداً.</summary>
    private static IReadOnlyList<ShellDef> FallbackDefs() => new[]
    {
        new ShellDef("cmd", "Command Prompt", "cmd.exe", "\r", true),
    };
}
