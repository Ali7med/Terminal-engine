namespace TerminalLauncher.Models;

/// <summary>
/// مشروع/تصنيف يجمع أوامر محفوظة تحت مظلّة واحدة. لكلّ مشروع لون مميّز تظهر به ترويسة شارة الأمر
/// في الشريط الجانبيّ ونقطة التصنيف في القائمة. الأمر قد يتبع أكثر من مشروع (وسوم متعدّدة).
/// </summary>
public sealed class Project
{
    /// <summary>اسم المشروع (فريد، حسّاس للحالة عند العرض لا عند المطابقة).</summary>
    public string Name { get; set; } = "";

    /// <summary>لون المشروع بصيغة #RRGGBB.</summary>
    public string Color { get; set; } = "#C96442";
}
