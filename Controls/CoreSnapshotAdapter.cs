using System;
using System.Collections.Generic;
using TerminalLauncher.Terminal;            // FrozenSpan, ScreenSnapshot, AnsiColor, TerminalStyle, CharAttr, BlockSnapshot
using CoreSnap = Terminal.Core.Screen.ScreenSnapshot;
using CoreRun = Terminal.Core.Screen.StyledRun;
using CoreColor = Terminal.Core.Vt.AnsiColor;
using CoreStyle = Terminal.Core.Vt.TerminalStyle;
using CoreFlags = Terminal.Core.Vt.TextStyleFlags;
using CoreBlock = Terminal.Core.Screen.BlockSnapshot;
using CoreBlockState = Terminal.Core.Screen.BlockState;

namespace TerminalLauncher.Controls;

/// <summary>
/// يحوّل لقطة محرّك <c>Terminal.Core</c> (<see cref="CoreRun"/>[]) إلى شكل
/// <see cref="ScreenSnapshot"/>/<see cref="FrozenSpan"/>[] الذي يعرضه <see cref="SkiaTerminalRenderer"/>،
/// فيستهلك الرندرر المحرّكَ المختبَر دون أي تغيير في كوده.
/// الأنواع على الطرفين متطابقة تصميمياً؛ هذا مجرّد ترجمة أسماء/تعدادات.
/// يُنقَل كلٌّ من كتل OSC 133 وروابط OSC 8 عبر هذا المسار (تُمرَّر Blocks فارغةً فقط إن لم تكن هناك كتل).
/// </summary>
public static class CoreSnapshotAdapter
{
    private static readonly IReadOnlyList<BlockSnapshot> NoBlocks = Array.Empty<BlockSnapshot>();

    /// <summary>يحوّل لقطة المحرّك الجديد إلى لقطة يفهمها العارض الحاليّ.</summary>
    public static ScreenSnapshot ToLauncher(CoreSnap s)
    {
        var lines = new FrozenSpan[s.Lines.Count][];
        for (int i = 0; i < s.Lines.Count; i++)
        {
            CoreRun[] src = s.Lines[i];
            var dst = new FrozenSpan[src.Length];
            for (int j = 0; j < src.Length; j++)
                dst[j] = new FrozenSpan(src[j].Text, ToStyle(src[j].Style), src[j].Link);
            lines[i] = dst;
        }

        // صفّ المؤشّر في المحرّك نسبيّ لأعلى الرؤية؛ نحوّله لفهرس داخل Lines (الذي يشمل scrollback).
        int cursorLine = (s.Lines.Count - s.Rows) + s.CursorRow;
        if (cursorLine < 0) cursorLine = 0;

        return new ScreenSnapshot(
            s.BaseLine,
            s.DirtyFromLine,
            lines,
            s.AltScreen,
            s.Blocks.Count == 0 ? NoBlocks : MapBlocks(s.Blocks),
            cursorLine,
            s.CursorCol,
            s.CursorVisible);
    }

    private static IReadOnlyList<BlockSnapshot> MapBlocks(IReadOnlyList<CoreBlock> src)
    {
        var list = new List<BlockSnapshot>(src.Count);
        foreach (var b in src)
        {
            var tb = new TerminalBlock
            {
                StartLine = b.StartLine,
                OutputStartLine = b.OutputStartLine,
                EndLine = b.EndLine,
                CommandText = b.CommandText,
                ExitCode = b.ExitCode,
                State = b.State switch
                {
                    CoreBlockState.Success => BlockState.Success,
                    CoreBlockState.Failed => BlockState.Failed,
                    _ => BlockState.Running,
                },
            };
            list.Add(new BlockSnapshot(tb));
        }
        return list;
    }

    private static TerminalStyle ToStyle(CoreStyle s) =>
        new(ToColor(s.Foreground), ToColor(s.Background), ToAttr(s.Flags));

    private static AnsiColor ToColor(CoreColor c) => c.Kind switch
    {
        CoreColor.ColorKind.Palette => AnsiColor.FromPalette(c.Index),
        CoreColor.ColorKind.Rgb => AnsiColor.FromRgb(c.R, c.G, c.B),
        _ => AnsiColor.Default,
    };

    private static CharAttr ToAttr(CoreFlags f)
    {
        CharAttr a = CharAttr.None;
        if ((f & CoreFlags.Bold) != 0) a |= CharAttr.Bold;
        if ((f & CoreFlags.Dim) != 0) a |= CharAttr.Dim;
        if ((f & CoreFlags.Italic) != 0) a |= CharAttr.Italic;
        if ((f & (CoreFlags.Underline | CoreFlags.DoubleUnderline)) != 0) a |= CharAttr.Underline;
        if ((f & CoreFlags.Inverse) != 0) a |= CharAttr.Inverse;
        if ((f & CoreFlags.Strikethrough) != 0) a |= CharAttr.Strikethrough;
        if ((f & CoreFlags.Blink) != 0) a |= CharAttr.Blink;
        return a;
    }
}
