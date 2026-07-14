namespace TerminalLauncher.Models;

/// <summary>
/// تاك (تصنيف) ملوّن يجمع عدّة مشاريع. العلاقة many-to-many: التاك يضمّ عدّة مشاريع، والمشروع قد
/// ينتمي لعدّة تاكات. اللون خاصّية التاك (المشروع بلا لون؛ يُعرَض بلون تاكه الأساس).
/// </summary>
public sealed class Tag
{
    /// <summary>اسم التاك (فريد، غير حسّاس للحالة عند المطابقة).</summary>
    public string Name { get; set; } = "";

    /// <summary>لون التاك بصيغة #RRGGBB.</summary>
    public string Color { get; set; } = "#C96442";
}
