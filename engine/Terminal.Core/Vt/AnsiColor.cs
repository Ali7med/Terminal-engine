namespace Terminal.Core.Vt;

/// <summary>Text style attributes carried by SGR (bit flags so several can combine).</summary>
[Flags]
public enum TextStyleFlags : ushort
{
    None = 0,
    Bold = 1 << 0,
    Dim = 1 << 1,
    Italic = 1 << 2,
    Underline = 1 << 3,
    Blink = 1 << 4,
    Inverse = 1 << 5,
    Hidden = 1 << 6,
    Strikethrough = 1 << 7,
    DoubleUnderline = 1 << 8,
}

/// <summary>
/// An ANSI colour: the terminal default (theme colour), a 256-colour palette index,
/// or a 24-bit true colour. Kept UI-free — the renderer (T-005) resolves it to a real
/// colour against the active theme/palette.
/// </summary>
public readonly struct AnsiColor : IEquatable<AnsiColor>
{
    public enum ColorKind : byte { Default, Palette, Rgb }

    public ColorKind Kind { get; }
    public byte Index { get; }      // valid when Kind == Palette (0..255)
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }          // valid when Kind == Rgb

    private AnsiColor(ColorKind kind, byte index, byte r, byte g, byte b)
    {
        Kind = kind;
        Index = index;
        R = r;
        G = g;
        B = b;
    }

    public static readonly AnsiColor Default = new(ColorKind.Default, 0, 0, 0, 0);

    public static AnsiColor FromPalette(int index) => new(ColorKind.Palette, (byte)(index & 0xFF), 0, 0, 0);

    public static AnsiColor FromRgb(byte r, byte g, byte b) => new(ColorKind.Rgb, 0, r, g, b);

    public bool Equals(AnsiColor other) =>
        Kind == other.Kind && Index == other.Index && R == other.R && G == other.G && B == other.B;

    public override bool Equals(object? obj) => obj is AnsiColor o && Equals(o);

    public override int GetHashCode() => HashCode.Combine((byte)Kind, Index, R, G, B);

    public static bool operator ==(AnsiColor a, AnsiColor b) => a.Equals(b);

    public static bool operator !=(AnsiColor a, AnsiColor b) => !a.Equals(b);
}

/// <summary>The current pen: foreground/background colour plus attribute flags.</summary>
public readonly struct TerminalStyle : IEquatable<TerminalStyle>
{
    public AnsiColor Foreground { get; }
    public AnsiColor Background { get; }
    public TextStyleFlags Flags { get; }

    public TerminalStyle(AnsiColor foreground, AnsiColor background, TextStyleFlags flags)
    {
        Foreground = foreground;
        Background = background;
        Flags = flags;
    }

    public static readonly TerminalStyle Default =
        new(AnsiColor.Default, AnsiColor.Default, TextStyleFlags.None);

    public TerminalStyle WithForeground(AnsiColor c) => new(c, Background, Flags);

    public TerminalStyle WithBackground(AnsiColor c) => new(Foreground, c, Flags);

    public TerminalStyle WithFlags(TextStyleFlags f) => new(Foreground, Background, f);

    public bool Has(TextStyleFlags flag) => (Flags & flag) == flag;

    public bool Equals(TerminalStyle other) =>
        Foreground.Equals(other.Foreground) && Background.Equals(other.Background) && Flags == other.Flags;

    public override bool Equals(object? obj) => obj is TerminalStyle o && Equals(o);

    public override int GetHashCode() => HashCode.Combine(Foreground, Background, (ushort)Flags);

    public static bool operator ==(TerminalStyle a, TerminalStyle b) => a.Equals(b);

    public static bool operator !=(TerminalStyle a, TerminalStyle b) => !a.Equals(b);
}
