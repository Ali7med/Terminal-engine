using System;

namespace TerminalLauncher.Terminal;

/// <summary>سمات النمط (SGR) المطبّقة على مقطع نصّي.</summary>
[Flags]
public enum CharAttr : byte
{
    None          = 0,
    Bold          = 1,
    Dim           = 2,
    Italic        = 4,
    Underline     = 8,
    Inverse       = 16,
    Strikethrough = 32,
    Blink         = 64,
}

/// <summary>
/// لون ANSI: افتراضي (لون الثيم)، فهرس لوحة (0..255)، أو لون حقيقي 24-bit.
/// </summary>
public readonly struct AnsiColor : IEquatable<AnsiColor>
{
    public enum ColorKind : byte { Default, Palette, Rgb }

    public readonly ColorKind Kind;
    public readonly byte Index;      // عند Palette
    public readonly byte R, G, B;    // عند Rgb

    private AnsiColor(ColorKind kind, byte index, byte r, byte g, byte b)
    {
        Kind = kind; Index = index; R = r; G = g; B = b;
    }

    public static readonly AnsiColor Default = new(ColorKind.Default, 0, 0, 0, 0);
    public static AnsiColor FromPalette(int index) => new(ColorKind.Palette, (byte)(index & 0xFF), 0, 0, 0);
    public static AnsiColor FromRgb(byte r, byte g, byte b) => new(ColorKind.Rgb, 0, r, g, b);

    public bool Equals(AnsiColor other) =>
        Kind == other.Kind && Index == other.Index && R == other.R && G == other.G && B == other.B;

    public override bool Equals(object? obj) => obj is AnsiColor o && Equals(o);
    public override int GetHashCode() => HashCode.Combine((byte)Kind, Index, R, G, B);
}

/// <summary>حالة النمط الجارية: لون مقدّمة/خلفية + أعلام السمات.</summary>
public readonly struct TerminalStyle : IEquatable<TerminalStyle>
{
    public readonly AnsiColor Fg;
    public readonly AnsiColor Bg;
    public readonly CharAttr Attr;

    public TerminalStyle(AnsiColor fg, AnsiColor bg, CharAttr attr)
    {
        Fg = fg; Bg = bg; Attr = attr;
    }

    public static readonly TerminalStyle Default = new(AnsiColor.Default, AnsiColor.Default, CharAttr.None);

    public TerminalStyle WithFg(AnsiColor c)   => new(c, Bg, Attr);
    public TerminalStyle WithBg(AnsiColor c)   => new(Fg, c, Attr);
    public TerminalStyle WithAttr(CharAttr a)  => new(Fg, Bg, a);

    public bool Equals(TerminalStyle other) => Fg.Equals(other.Fg) && Bg.Equals(other.Bg) && Attr == other.Attr;
    public override bool Equals(object? obj) => obj is TerminalStyle o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Fg, Bg, (byte)Attr);
}
