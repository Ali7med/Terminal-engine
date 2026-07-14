using System.Collections.Generic;
using System.Linq;

namespace TerminalLauncher.Models;

/// <summary>
/// أمر مثبَّت داخل مشروع. قد يكون خطوة واحدة أو عدّة خطوات تُنفَّذ بالتوالي في نفس الأمر.
/// يرث فولدر المشروع افتراضياً، ويمكن تجاوزه عبر <see cref="Folder"/>.
/// </summary>
public sealed class ProjectCommand
{
    /// <summary>اسم العرض في اللوحة (إن غاب يُعرَض أوّل خطوة).</summary>
    public string Label { get; set; } = "";

    /// <summary>خطوات التنفيذ — كلّ خطوة سطر يُرسَل للصدفة بالتتابع. خطوة واحدة = أمر عاديّ.</summary>
    public List<string> Steps { get; set; } = new();

    /// <summary>تجاوز فولدر التنفيذ لهذا الأمر (null = يرث فولدر المشروع).</summary>
    public string? Folder { get; set; }

    /// <summary>ما يُعرَض: الاسم إن وُجد، وإلّا أوّل خطوة.</summary>
    public string Display => !string.IsNullOrWhiteSpace(Label)
        ? Label
        : (Steps.Count > 0 ? Steps[0] : "");

    /// <summary>هل الأمر متعدّد الخطوات؟</summary>
    public bool IsMultiStep => Steps.Count > 1;

    /// <summary>الخطوات كنصّ واحد بأسطر (للعرض والتحرير).</summary>
    public string StepsText => string.Join("\n", Steps);

    /// <summary>مفتاح المطابقة للتكرار: الخطوات مُشذَّبة مجموعةً بسطر (حسّاس للحالة).</summary>
    public string DedupKey => string.Join("\n", Steps.Select(s => s.Trim()));
}
