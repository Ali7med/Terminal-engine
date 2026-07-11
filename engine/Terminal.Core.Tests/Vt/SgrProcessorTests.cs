using Terminal.Core.Vt;

namespace Terminal.Core.Tests.Vt;

public class SgrProcessorTests
{
    private static TerminalStyle Sgr(string paramText, TerminalStyle? from = null) =>
        SgrProcessor.Apply(from ?? TerminalStyle.Default, VtParams.Parse(paramText));

    [Fact]
    public void Empty_parameters_reset_to_default()
    {
        var start = new TerminalStyle(AnsiColor.FromPalette(1), AnsiColor.FromPalette(2), TextStyleFlags.Bold);
        Assert.Equal(TerminalStyle.Default, SgrProcessor.Apply(start, VtParams.Parse("")));
    }

    [Fact]
    public void Zero_resets_all()
    {
        var start = new TerminalStyle(AnsiColor.FromRgb(1, 2, 3), AnsiColor.FromPalette(9), TextStyleFlags.Italic);
        Assert.Equal(TerminalStyle.Default, Sgr("0", start));
    }

    [Fact]
    public void Attributes_set_and_clear()
    {
        var s = Sgr("1;3;4;7;9");
        Assert.True(s.Has(TextStyleFlags.Bold));
        Assert.True(s.Has(TextStyleFlags.Italic));
        Assert.True(s.Has(TextStyleFlags.Underline));
        Assert.True(s.Has(TextStyleFlags.Inverse));
        Assert.True(s.Has(TextStyleFlags.Strikethrough));

        var cleared = Sgr("22;23;24;27;29", s);
        Assert.False(cleared.Has(TextStyleFlags.Bold));
        Assert.False(cleared.Has(TextStyleFlags.Italic));
        Assert.False(cleared.Has(TextStyleFlags.Underline));
        Assert.False(cleared.Has(TextStyleFlags.Inverse));
        Assert.False(cleared.Has(TextStyleFlags.Strikethrough));
    }

    [Fact]
    public void Basic_16_colors()
    {
        var red = Sgr("31");
        Assert.Equal(AnsiColor.ColorKind.Palette, red.Foreground.Kind);
        Assert.Equal(1, red.Foreground.Index);

        var bgGreen = Sgr("42");
        Assert.Equal(2, bgGreen.Background.Index);

        var brightBlue = Sgr("94");
        Assert.Equal(12, brightBlue.Foreground.Index); // 94-90+8

        var brightBg = Sgr("101");
        Assert.Equal(9, brightBg.Background.Index); // 101-100+8
    }

    [Fact]
    public void Default_color_codes()
    {
        var s = Sgr("39;49", new TerminalStyle(AnsiColor.FromPalette(1), AnsiColor.FromPalette(2), TextStyleFlags.None));
        Assert.Equal(AnsiColor.Default, s.Foreground);
        Assert.Equal(AnsiColor.Default, s.Background);
    }

    [Fact]
    public void Palette_256_semicolon_form()
    {
        var s = Sgr("38;5;208");
        Assert.Equal(AnsiColor.ColorKind.Palette, s.Foreground.Kind);
        Assert.Equal(208, s.Foreground.Index);
    }

    [Fact]
    public void TrueColor_semicolon_form()
    {
        var s = Sgr("38;2;10;20;30");
        Assert.Equal(AnsiColor.ColorKind.Rgb, s.Foreground.Kind);
        Assert.Equal(10, s.Foreground.R);
        Assert.Equal(20, s.Foreground.G);
        Assert.Equal(30, s.Foreground.B);
    }

    [Fact]
    public void TrueColor_background_then_bold_in_one_sequence()
    {
        var s = Sgr("1;48;2;255;128;0;38;5;15");
        Assert.True(s.Has(TextStyleFlags.Bold));
        Assert.Equal(AnsiColor.ColorKind.Rgb, s.Background.Kind);
        Assert.Equal(255, s.Background.R);
        Assert.Equal(128, s.Background.G);
        Assert.Equal(0, s.Background.B);
        Assert.Equal(15, s.Foreground.Index);
    }

    [Fact]
    public void TrueColor_colon_form()
    {
        var s = Sgr("38:2:0:255:128");
        Assert.Equal(AnsiColor.ColorKind.Rgb, s.Foreground.Kind);
        Assert.Equal(0, s.Foreground.R);
        Assert.Equal(255, s.Foreground.G);
        Assert.Equal(128, s.Foreground.B);
    }

    [Fact]
    public void Palette_colon_form()
    {
        var s = Sgr("38:5:196");
        Assert.Equal(AnsiColor.ColorKind.Palette, s.Foreground.Kind);
        Assert.Equal(196, s.Foreground.Index);
    }

    [Fact]
    public void Rgb_values_are_clamped()
    {
        var s = Sgr("38;2;999;300;5");
        Assert.Equal(255, s.Foreground.R);
        Assert.Equal(255, s.Foreground.G);
        Assert.Equal(5, s.Foreground.B);
    }

    [Fact]
    public void Colon_underline_double()
    {
        var s = Sgr("4:2");
        Assert.True(s.Has(TextStyleFlags.DoubleUnderline));
        Assert.False(s.Has(TextStyleFlags.Underline));
    }
}
