using System.Text;

namespace Terminal.Core.Screen;

/// <summary>
/// A single grid cell: a Unicode scalar plus an index into the owning buffer's
/// <see cref="StyleTable"/>. Attributes are interned (an integer id) rather than repeated
/// in every cell, keeping the grid compact (guide T-004.1). The right-hand column of a
/// double-width glyph is represented by a "wide trailing" cell that carries no glyph.
/// </summary>
public readonly struct Cell : IEquatable<Cell>
{
    /// <summary>Sentinel codepoint marking the second column occupied by a double-width glyph.</summary>
    internal const int WideTrailingSentinel = -1;

    private readonly int _codepoint; // Unicode scalar value, or WideTrailingSentinel

    public Cell(Rune rune, int styleId, int linkId = 0)
    {
        _codepoint = rune.Value;
        StyleId = styleId;
        LinkId = linkId;
    }

    private Cell(int codepoint, int styleId, int linkId)
    {
        _codepoint = codepoint;
        StyleId = styleId;
        LinkId = linkId;
    }

    /// <summary>Index into the owning buffer's <see cref="StyleTable"/> (0 = default style).</summary>
    public int StyleId { get; }

    /// <summary>Index into the owning buffer's <see cref="LinkTable"/> (0 = no OSC 8 hyperlink).</summary>
    public int LinkId { get; }

    /// <summary>A blank cell: a space in the default style.</summary>
    public static readonly Cell Blank = new(new Rune(' '), 0);

    /// <summary>True for the continuation (right half) of a double-width glyph.</summary>
    public bool IsWideTrailing => _codepoint == WideTrailingSentinel;

    /// <summary>The scalar value in this cell (0 for a wide-trailing cell).</summary>
    public int Codepoint => _codepoint == WideTrailingSentinel ? 0 : _codepoint;

    /// <summary>The rune in this cell (NUL for a wide-trailing cell).</summary>
    public Rune Rune => Rune.TryCreate(Codepoint, out var rune) ? rune : Rune.ReplacementChar;

    /// <summary>Creates the trailing (right) half of a double-width glyph in the given style/link.</summary>
    internal static Cell WideTrailing(int styleId, int linkId = 0) => new(WideTrailingSentinel, styleId, linkId);

    public bool Equals(Cell other) =>
        _codepoint == other._codepoint && StyleId == other.StyleId && LinkId == other.LinkId;

    public override bool Equals(object? obj) => obj is Cell c && Equals(c);

    public override int GetHashCode() => HashCode.Combine(_codepoint, StyleId, LinkId);

    public static bool operator ==(Cell a, Cell b) => a.Equals(b);

    public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
}
