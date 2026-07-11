using System.Collections.Generic;
using Terminal.Core.Vt;

namespace Terminal.Core.Screen;

/// <summary>A run of consecutive characters sharing one style — the unit the renderer draws.</summary>
public readonly struct StyledRun : IEquatable<StyledRun>
{
    public StyledRun(string text, TerminalStyle style, string? link = null)
    {
        Text = text;
        Style = style;
        Link = link;
    }

    public string Text { get; }
    public TerminalStyle Style { get; }

    /// <summary>OSC 8 hyperlink target for this run, or <c>null</c> if it is not a link.</summary>
    public string? Link { get; }

    public bool Equals(StyledRun other) =>
        string.Equals(Text, other.Text, StringComparison.Ordinal)
        && Style.Equals(other.Style)
        && string.Equals(Link, other.Link, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is StyledRun r && Equals(r);

    public override int GetHashCode() =>
        HashCode.Combine(
            Text is null ? 0 : StringComparer.Ordinal.GetHashCode(Text),
            Style,
            Link is null ? 0 : StringComparer.Ordinal.GetHashCode(Link));

    public static bool operator ==(StyledRun a, StyledRun b) => a.Equals(b);

    public static bool operator !=(StyledRun a, StyledRun b) => !a.Equals(b);
}

/// <summary>
/// An immutable view of the screen for the render thread (guide T-004.5).
/// <see cref="DirtyFromLine"/> is the smallest absolute line number that changed since the
/// previous snapshot (<see cref="long.MaxValue"/> = nothing changed), letting the renderer
/// rebuild only the tail. <see cref="AltScreen"/> signals a full-screen application is active.
/// For the main screen <see cref="Lines"/> is scrollback followed by the visible rows; for the
/// alternate screen it is the visible rows only (no scrollback by convention).
/// </summary>
public sealed class ScreenSnapshot
{
    public ScreenSnapshot(
        int rows, int cols, int cursorRow, int cursorCol, bool cursorVisible,
        bool altScreen, long baseLine, long dirtyFromLine, IReadOnlyList<StyledRun[]> lines,
        IReadOnlyList<BlockSnapshot>? blocks = null)
    {
        Rows = rows;
        Cols = cols;
        CursorRow = cursorRow;
        CursorCol = cursorCol;
        CursorVisible = cursorVisible;
        AltScreen = altScreen;
        BaseLine = baseLine;
        DirtyFromLine = dirtyFromLine;
        Lines = lines;
        Blocks = blocks ?? Array.Empty<BlockSnapshot>();
    }

    public int Rows { get; }
    public int Cols { get; }
    public int CursorRow { get; }
    public int CursorCol { get; }
    public bool CursorVisible { get; }
    public bool AltScreen { get; }

    /// <summary>Absolute line number of the first entry in <see cref="Lines"/>.</summary>
    public long BaseLine { get; }

    /// <summary>Smallest absolute line number changed since the previous snapshot.</summary>
    public long DirtyFromLine { get; }

    public IReadOnlyList<StyledRun[]> Lines { get; }

    /// <summary>Command blocks (OSC 133 / heuristic) tracked for the current session; empty if none.</summary>
    public IReadOnlyList<BlockSnapshot> Blocks { get; }
}
