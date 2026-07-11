using System;

namespace TerminalLauncher.Models;

/// <summary>
/// عنصر في لوحة الأوامر (Ctrl+Shift+P): فعل ثابت أو أمر محفوظ.
/// <see cref="Invoke"/> ينفّذ السلوك عند اختياره.
/// </summary>
public sealed class CommandPaletteItem
{
    /// <summary>أيقونة Segoe MDL2 Assets.</summary>
    public string Icon { get; init; } = "";

    /// <summary>العنوان المعروض (عربي).</summary>
    public string Title { get; init; } = "";

    /// <summary>تلميح جانبيّ (اختصار أو مسار).</summary>
    public string Hint { get; init; } = "";

    /// <summary>السلوك المُنفَّذ عند الاختيار.</summary>
    public Action Invoke { get; init; } = static () => { };
}
