using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using TerminalLauncher.Models;

namespace TerminalLauncher.Services;

/// <summary>
/// سجلّ التاكات الحيّ (اسم → لون) المشترك بين الواجهة ومحوّلات الربط. ثابت كي تصله المحوّلات بلا سياق.
/// الحفظ على القرص مسؤوليّة المستهلِك عبر حدث <see cref="Changed"/>.
/// </summary>
public static class TagService
{
    private static readonly List<Tag> _tags = new();

    /// <summary>لوحة ألوان التاكات: تُدوَّر تلقائياً للتاكات الجديدة وتُعرَض في المنتقي. تغطّي الطيف كاملاً.</summary>
    public static readonly string[] Palette =
    {
        "#C96442", "#E0603F", "#F97316", "#F59E0B", "#EAB308",
        "#84CC16", "#22C55E", "#10B981", "#14B8A6", "#06B6D4",
        "#0EA5E9", "#3B82F6", "#6366F1", "#8B5CF6", "#A78BFA",
        "#D946EF", "#EC4899", "#F43F5E", "#EF4444", "#64748B",
    };

    /// <summary>يُطلَق عند أيّ تغيير في التاكات (إضافة/إعادة تلوين/حذف) — للحفظ وإعادة رسم الواجهة.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<Tag> All => _tags;

    /// <summary>يستبدل المحتوى بتاكات محمَّلة من القرص (بلا إطلاق حدث).</summary>
    public static void Initialize(IEnumerable<Tag> tags)
    {
        _tags.Clear();
        _tags.AddRange(tags);
    }

    /// <summary>يعيد تاكاً بالاسم أو null (مطابقة غير حسّاسة للحالة).</summary>
    public static Tag? Find(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? null
            : _tags.FirstOrDefault(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>اللون التلقائيّ للتاك التالي (يُدوَّر حسب العدد الحاليّ).</summary>
    public static string NextAutoColor => Palette[_tags.Count % Palette.Length];

    /// <summary>يعيد تاكاً بالاسم، ويُنشئه باللون المعطى (أو التلقائيّ إن null) إن لم يوجد (يُطلِق <see cref="Changed"/>).</summary>
    public static Tag GetOrCreate(string name, string? colorHex = null)
    {
        var t = GetOrCreateSilent(name, colorHex);
        Changed?.Invoke();
        return t;
    }

    /// <summary>يضيف تاكاً بلا إطلاق حدث (للترحيل الدفعيّ)؛ يعيد الموجود إن كان الاسم مستعمَلاً.</summary>
    public static Tag GetOrCreateSilent(string name, string? colorHex = null)
    {
        name = name.Trim();
        var existing = Find(name);
        if (existing != null) return existing;
        var created = new Tag { Name = name, Color = colorHex ?? NextAutoColor };
        _tags.Add(created);
        return created;
    }

    /// <summary>يعيد تسمية تاك (يُطلِق <see cref="Changed"/>). لا يفعل شيئاً إن تعارض الاسم الجديد أو كان فارغاً.</summary>
    public static void Rename(string oldName, string newName)
    {
        newName = (newName ?? "").Trim();
        var t = Find(oldName);
        if (t == null || newName.Length == 0) return;
        if (!string.Equals(t.Name, newName, StringComparison.OrdinalIgnoreCase) && Find(newName) != null) return;
        t.Name = newName;
        Changed?.Invoke();
    }

    /// <summary>يغيّر لون تاك قائم (يُطلِق <see cref="Changed"/>).</summary>
    public static void SetColor(string name, string hex)
    {
        var t = Find(name);
        if (t == null || string.Equals(t.Color, hex, StringComparison.OrdinalIgnoreCase)) return;
        t.Color = hex;
        Changed?.Invoke();
    }

    /// <summary>يحذف تاكاً بالاسم (يُطلِق <see cref="Changed"/> إن وُجِد). فكّ ربطه من المشاريع مسؤوليّة المستدعي.</summary>
    public static void Remove(string name)
    {
        int i = _tags.FindIndex(t => string.Equals(t.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        _tags.RemoveAt(i);
        Changed?.Invoke();
    }

    /// <summary>لون تاك بالاسم، أو null إن لم يوجد.</summary>
    public static Color? ColorOf(string? name)
    {
        var t = Find(name);
        return t != null ? Parse(t.Color) : (Color?)null;
    }

    private static Color Parse(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Gray; }
    }
}
