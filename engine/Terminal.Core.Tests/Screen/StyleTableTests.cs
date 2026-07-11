using Terminal.Core.Screen;
using Terminal.Core.Vt;

namespace Terminal.Core.Tests.Screen;

public class StyleTableTests
{
    [Fact]
    public void Default_style_is_id_zero()
    {
        var table = new StyleTable();
        Assert.Equal(1, table.Count);
        Assert.Equal(0, table.Intern(TerminalStyle.Default));
        Assert.Equal(TerminalStyle.Default, table.Resolve(0));
    }

    [Fact]
    public void Equal_styles_intern_to_the_same_id()
    {
        var table = new StyleTable();
        var red = new TerminalStyle(AnsiColor.FromPalette(1), AnsiColor.Default, TextStyleFlags.Bold);
        var redAgain = new TerminalStyle(AnsiColor.FromPalette(1), AnsiColor.Default, TextStyleFlags.Bold);

        int id = table.Intern(red);
        Assert.Equal(id, table.Intern(redAgain));
        Assert.Equal(2, table.Count); // default + red
        Assert.Equal(red, table.Resolve(id));
    }

    [Fact]
    public void Distinct_styles_get_distinct_ids()
    {
        var table = new StyleTable();
        int a = table.Intern(new TerminalStyle(AnsiColor.FromRgb(10, 20, 30), AnsiColor.Default, TextStyleFlags.None));
        int b = table.Intern(new TerminalStyle(AnsiColor.FromRgb(30, 20, 10), AnsiColor.Default, TextStyleFlags.None));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Unknown_id_resolves_to_default()
    {
        var table = new StyleTable();
        Assert.Equal(TerminalStyle.Default, table.Resolve(999));
        Assert.Equal(TerminalStyle.Default, table.Resolve(-1));
    }
}
