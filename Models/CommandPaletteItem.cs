using System;

namespace TerminalLauncher.Models;

/// <summary>فئة عنصر البحث المتقدّم — تُستعمل لرقاقات التصفية في أعلى اللوحة.</summary>
public enum PaletteCategory { Action, Project, Command, Tab, Shell }

/// <summary>
/// عنصر في لوحة البحث المتقدّم (Ctrl+Shift+P أو حقل بحث الرأس): فعل · مشروع · أمر محفوظ ·
/// تبويب مفتوح · بروفايل صدفة. <see cref="Invoke"/> ينفّذ السلوك عند اختياره.
/// </summary>
public sealed class CommandPaletteItem
{
    /// <summary>فئة العنصر (لرقاقات التصفية).</summary>
    public PaletteCategory Category { get; init; } = PaletteCategory.Action;

    /// <summary>أيقونة Segoe MDL2 Assets.</summary>
    public string Icon { get; init; } = "";

    /// <summary>العنوان المعروض (عربي).</summary>
    public string Title { get; init; } = "";

    /// <summary>تلميح جانبيّ (اختصار أو مسار).</summary>
    public string Hint { get; init; } = "";

    /// <summary>السلوك المُنفَّذ عند الاختيار.</summary>
    public Action Invoke { get; init; } = static () => { };
}
