using Terminal.Core.Vt;

namespace Terminal.Core.Tests.Vt;

public class ColorModelTests
{
    [Fact]
    public void AnsiColor_factories_set_kind()
    {
        Assert.Equal(AnsiColor.ColorKind.Default, AnsiColor.Default.Kind);

        var pal = AnsiColor.FromPalette(200);
        Assert.Equal(AnsiColor.ColorKind.Palette, pal.Kind);
        Assert.Equal(200, pal.Index);

        var rgb = AnsiColor.FromRgb(10, 20, 30);
        Assert.Equal(AnsiColor.ColorKind.Rgb, rgb.Kind);
        Assert.Equal((10, 20, 30), (rgb.R, rgb.G, rgb.B));
    }

    [Fact]
    public void AnsiColor_palette_index_wraps_to_byte()
    {
        Assert.Equal(0, AnsiColor.FromPalette(256).Index);
        Assert.Equal(1, AnsiColor.FromPalette(257).Index);
    }

    [Fact]
    public void AnsiColor_equality_and_operators()
    {
        var a = AnsiColor.FromRgb(1, 2, 3);
        var b = AnsiColor.FromRgb(1, 2, 3);
        var c = AnsiColor.FromRgb(1, 2, 4);

        Assert.True(a == b);
        Assert.False(a == c);
        Assert.True(a != c);
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals("not a color"));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void TerminalStyle_with_methods_are_immutable_updates()
    {
        var s = TerminalStyle.Default
            .WithForeground(AnsiColor.FromPalette(4))
            .WithBackground(AnsiColor.FromRgb(9, 9, 9))
            .WithFlags(TextStyleFlags.Bold | TextStyleFlags.Italic);

        Assert.Equal(AnsiColor.FromPalette(4), s.Foreground);
        Assert.Equal(AnsiColor.FromRgb(9, 9, 9), s.Background);
        Assert.True(s.Has(TextStyleFlags.Bold));
        Assert.True(s.Has(TextStyleFlags.Italic));
        Assert.False(s.Has(TextStyleFlags.Underline));

        // The default is untouched (value type, non-mutating helpers).
        Assert.Equal(AnsiColor.Default, TerminalStyle.Default.Foreground);
    }

    [Fact]
    public void TerminalStyle_equality_and_operators()
    {
        var a = new TerminalStyle(AnsiColor.FromPalette(1), AnsiColor.Default, TextStyleFlags.Bold);
        var b = new TerminalStyle(AnsiColor.FromPalette(1), AnsiColor.Default, TextStyleFlags.Bold);
        var c = new TerminalStyle(AnsiColor.FromPalette(1), AnsiColor.Default, TextStyleFlags.Dim);

        Assert.True(a == b);
        Assert.True(a != c);
        Assert.False(a == c);
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals(42));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
