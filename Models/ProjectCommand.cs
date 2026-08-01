using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

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
    [JsonIgnore]
    public string Display => !string.IsNullOrWhiteSpace(Label)
        ? Label
        : (Steps.Count > 0 ? Steps[0] : "");

    /// <summary>هل الأمر متعدّد الخطوات؟</summary>
    [JsonIgnore]
    public bool IsMultiStep => Steps.Count > 1;

    /// <summary>الخطوات كنصّ واحد بأسطر (للعرض والتحرير).</summary>
    [JsonIgnore]
    public string StepsText => string.Join("\n", Steps);

    /// <summary>
    /// مفتاح المطابقة للتكرار: الخطوات <b>والفولدر والاسم</b> معاً.
    ///
    /// <para><b>لماذا الثلاثة:</b> بالخطوات وحدها كان <c>npm run dev</c> في فولدر الفرونت يمنع
    /// إضافة <c>npm run dev</c> في فولدر آخر من المشروع نفسه — وهما أمران مختلفان تماماً. الغرض
    /// من الفحص منعُ التكرار الحرفيّ (زرّ «أضف الأمر الحاليّ» مرّتين)، لا منعُ تكرار نصّ الأمر.</para>
    /// </summary>
    [JsonIgnore]
    public string DedupKey => string.Join("\n", Steps.Select(s => s.Trim()))
        + "\u0000" + (Folder ?? "").Trim()
        + "\u0000" + Label.Trim();
}
