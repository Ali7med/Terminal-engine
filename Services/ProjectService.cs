using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using TerminalLauncher.Models;

namespace TerminalLauncher.Services;

/// <summary>
/// سجلّ المشاريع/التصنيفات الحيّ (اسم → لون) المشترك بين الواجهة ومحوّلات الربط (Converters). ثابت كي
/// تصله المحوّلات بلا سياق. الحفظ على القرص مسؤوليّة المستهلِك عبر حدث <see cref="Changed"/>.
/// </summary>
public static class ProjectService
{
    private static readonly List<Project> _projects = new();

    /// <summary>
    /// لوحة ألوان المشاريع: تُدوَّر تلقائياً للمشاريع الجديدة، وتُعرَض في منتقي الألوان. متناسقة مع لكنات
    /// الثيمات وتغطّي الطيف كاملاً. يمكن للمستخدم أيضاً إدخال لون مخصّص (#RRGGBB) خارج هذه اللوحة.
    /// </summary>
    public static readonly string[] Palette =
    {
        "#C96442", "#E0603F", "#F97316", "#F59E0B", "#EAB308",
        "#84CC16", "#22C55E", "#10B981", "#14B8A6", "#06B6D4",
        "#0EA5E9", "#3B82F6", "#6366F1", "#8B5CF6", "#A78BFA",
        "#D946EF", "#EC4899", "#F43F5E", "#EF4444", "#64748B",
    };

    /// <summary>يُطلَق عند أيّ تغيير في قائمة المشاريع (إضافة/إعادة تلوين) — للحفظ وإعادة رسم الواجهة.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<Project> All => _projects;

    /// <summary>يستبدل المحتوى بمشاريع محمَّلة من القرص (بلا إطلاق حدث الحفظ لتفادي حلقة).</summary>
    public static void Initialize(IEnumerable<Project> projects)
    {
        _projects.Clear();
        _projects.AddRange(projects);
    }

    /// <summary>يعيد مشروعاً بالاسم أو null (مطابقة غير حسّاسة للحالة).</summary>
    public static Project? Find(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? null
            : _projects.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>اللون التلقائيّ للمشروع التالي (يُدوَّر حسب عدد المشاريع الحاليّ) — لمعاينة الإنشاء.</summary>
    public static string NextAutoColor => Palette[_projects.Count % Palette.Length];

    /// <summary>يعيد مشروعاً بالاسم، ويُنشئه بلون تلقائيّ من اللوحة إن لم يوجد (يُطلِق <see cref="Changed"/>).</summary>
    public static Project GetOrCreate(string name) => GetOrCreate(name, null);

    /// <summary>يعيد مشروعاً بالاسم، ويُنشئه باللون المعطى (أو التلقائيّ إن null) إن لم يوجد (يُطلِق <see cref="Changed"/>).</summary>
    public static Project GetOrCreate(string name, string? colorHex)
    {
        name = name.Trim();
        var existing = Find(name);
        if (existing != null) return existing;
        var created = new Project { Name = name, Color = colorHex ?? NextAutoColor };
        _projects.Add(created);
        Changed?.Invoke();
        return created;
    }

    /// <summary>يحذف مشروعاً بالاسم (يُطلِق <see cref="Changed"/> إن وُجِد). لا يمسّ وسوم الأوامر — ذلك مسؤوليّة المستدعي.</summary>
    public static void Remove(string name)
    {
        int i = _projects.FindIndex(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        _projects.RemoveAt(i);
        Changed?.Invoke();
    }

    /// <summary>يغيّر لون مشروع قائم (يُطلِق <see cref="Changed"/>).</summary>
    public static void SetColor(string name, string hex)
    {
        var p = Find(name);
        if (p == null || string.Equals(p.Color, hex, StringComparison.OrdinalIgnoreCase)) return;
        p.Color = hex;
        Changed?.Invoke();
    }

    /// <summary>لون مشروع بالاسم، أو null إن لم يوجد.</summary>
    public static Color? ColorOf(string? name)
    {
        var p = Find(name);
        return p != null ? Parse(p.Color) : (Color?)null;
    }

    /// <summary>لون المشروع الأساس للأمر (أوّل وسم)، أو null إن بلا تصنيف.</summary>
    public static Color? PrimaryColor(CommandEntry entry)
        => entry.Tags.Count > 0 ? ColorOf(entry.Tags[0]) : null;

    private static Color Parse(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Gray; }
    }
}
