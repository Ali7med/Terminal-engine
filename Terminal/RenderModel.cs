using System;
using System.Collections.Generic;

namespace TerminalLauncher.Terminal;

// نموذج العرض الذي يستهلكه SkiaTerminalRenderer (مستقلّ عن المحرّك).
// المحرّك الفعليّ صار Terminal.Core؛ يحوّل CoreSnapshotAdapter لقطته إلى هذه الأنواع.

/// <summary>مقطع نصّي مُجمّد (نصّ + نمط + رابط تشعّبيّ اختياريّ) — الوحدة التي يعرضها العارض.</summary>
public readonly struct FrozenSpan
{
    public readonly string Text;
    public readonly TerminalStyle Style;
    public readonly string? Hyperlink;   // OSC 8 (null = لا رابط)
    public FrozenSpan(string text, TerminalStyle style, string? hyperlink = null)
    {
        Text = text; Style = style; Hyperlink = hyperlink;
    }
}

/// <summary>حالة كتلة (Block): أمر واحد ومخرجاته كوحدة نمط Warp.</summary>
public enum BlockState
{
    /// <summary>الأمر يُنفَّذ الآن (لا رمز خروج بعد).</summary>
    Running,
    /// <summary>انتهى بنجاح (رمز خروج = 0).</summary>
    Success,
    /// <summary>انتهى بفشل (رمز خروج ≠ 0).</summary>
    Failed,
}

/// <summary>
/// كتلة (Block): أمر واحد ومخرجاته كوحدة نمط Warp قابلة للنسخ/القفز.
/// المدى بالسطر المطلق (نفس مقياس <see cref="ScreenSnapshot.BaseLine"/>).
/// </summary>
public sealed class TerminalBlock
{
    /// <summary>رقم السطر المطلق لأوّل سطر في الكتلة (سطر الـ prompt/الأمر).</summary>
    public long StartLine { get; internal set; }

    /// <summary>رقم السطر المطلق لأوّل سطر من المخرجات (سطر الأمر أو ما بعده).</summary>
    public long OutputStartLine { get; internal set; }

    /// <summary>رقم السطر المطلق التالي لآخر سطر في الكتلة (حصري)؛ <c>long.MaxValue</c> = ما زالت مفتوحة.</summary>
    public long EndLine { get; internal set; } = long.MaxValue;

    /// <summary>نصّ الأمر المُنفَّذ (إن عُرِف من الاستدلال).</summary>
    public string CommandText { get; internal set; } = "";

    /// <summary>رمز الخروج إن توفّر (من OSC 133;D;code).</summary>
    public int? ExitCode { get; internal set; }

    /// <summary>حالة الكتلة (تعمل/نجاح/فشل).</summary>
    public BlockState State { get; internal set; } = BlockState.Running;
}

/// <summary>لقطة كتلة مُجمّدة (للعرض على خيط الواجهة، غير قابلة للتغيير).</summary>
public sealed class BlockSnapshot
{
    public long StartLine { get; }
    public long OutputStartLine { get; }
    public long EndLine { get; }
    public string CommandText { get; }
    public int? ExitCode { get; }
    public BlockState State { get; }

    public BlockSnapshot(TerminalBlock b)
    {
        StartLine = b.StartLine;
        OutputStartLine = b.OutputStartLine;
        EndLine = b.EndLine;
        CommandText = b.CommandText;
        ExitCode = b.ExitCode;
        State = b.State;
    }
}

/// <summary>
/// لقطة غير قابلة للتغيير من الشاشة للعرض على خيط الواجهة.
/// <see cref="DirtyFrom"/> = أصغر رقم سطر (مطلق) تغيّر منذ اللقطة السابقة (long.MaxValue = لا تغيير).
/// <see cref="AltScreen"/> = الشاشة البديلة نشطة ⇒ يعطّل العارض تزيين الكتل.
/// </summary>
public sealed class ScreenSnapshot
{
    public long BaseLine { get; }
    public long DirtyFrom { get; }
    public IReadOnlyList<FrozenSpan[]> Lines { get; }
    public bool AltScreen { get; }
    public IReadOnlyList<BlockSnapshot> Blocks { get; }

    /// <summary>فهرس سطر المؤشّر داخل <see cref="Lines"/> (يشمل scrollback).</summary>
    public int CursorLine { get; }
    /// <summary>عمود المؤشّر (0-مبنيّ).</summary>
    public int CursorColumn { get; }
    /// <summary>هل المؤشّر ظاهر (DECTCEM).</summary>
    public bool CursorVisible { get; }

    public ScreenSnapshot(long baseLine, long dirtyFrom, IReadOnlyList<FrozenSpan[]> lines,
        bool altScreen, IReadOnlyList<BlockSnapshot> blocks,
        int cursorLine, int cursorColumn, bool cursorVisible)
    {
        BaseLine = baseLine; DirtyFrom = dirtyFrom; Lines = lines;
        AltScreen = altScreen; Blocks = blocks;
        CursorLine = cursorLine; CursorColumn = cursorColumn; CursorVisible = cursorVisible;
    }
}
