using System;
using System.Collections.Generic;
using System.Linq;

namespace TerminalLauncher.Models;

/// <summary>
/// مشروع: الكيان الأساس. يحمل باثاً (فولدر عمل) + اسماً + مجموعة أوامر مثبَّتة + تاكات (تصنيفات ملوّنة،
/// many-to-many). المشروع نفسه بلا لون — يُعرَض بلون تاكه الأساس.
/// </summary>
public sealed class Project
{
    /// <summary>اسم المشروع (فريد، حسّاس للحالة عند العرض لا عند المطابقة).</summary>
    public string Name { get; set; } = "";

    /// <summary>الباث (مجلّد العمل) الافتراضيّ لأوامر المشروع. قد يتجاوزه أمر منفرد.</summary>
    public string Folder { get; set; } = "";

    /// <summary>مفتاح الصدفة الافتراضيّة للمشروع (cmd | powershell | bash…). فارغ = الافتراضيّة العامّة.</summary>
    public string Shell { get; set; } = "";

    /// <summary>أسماء التاكات التي ينتمي إليها المشروع (many-to-many). الأوّل يُعتبَر التاك الأساس (لون العرض).</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>الأوامر المثبَّتة في المشروع (كلّ أمر قد يكون خطوة أو عدّة خطوات متتالية).</summary>
    public List<ProjectCommand> Commands { get; set; } = new();

    /// <summary>
    /// مهجور — يُقرأ فقط أثناء ترحيل V2 لنقل لون المشروع القديم إلى تاك. لا يُستعمل في الواجهة الجديدة.
    /// </summary>
    public string Color { get; set; } = "";

    /// <summary>التاك الأساس (أوّل تاك) أو null إن بلا تصنيف — لون العرض يُشتقّ منه.</summary>
    public string? PrimaryTag => Tags.Count > 0 ? Tags[0] : null;

    /// <summary>هل للمشروع تاك واحد على الأقلّ؟</summary>
    public bool HasTags => Tags.Count > 0;

    /// <summary>هل ينتمي المشروع للتاك المسمّى (مطابقة غير حسّاسة للحالة)؟</summary>
    public bool HasTag(string tag)
        => Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>الحرف الأوّل من الاسم (لمربّع المشروع في الشريط الجانبيّ المطويّ).</summary>
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim().Substring(0, 1).ToUpperInvariant();
}
