using System.Text;
using Terminal.Core.Screen;
using Terminal.Core.Vt;

namespace Terminal.Core.Tests.Screen;

public class CellAndStyledRunTests
{
    [Fact]
    public void Cell_equality_considers_rune_and_style()
    {
        var a = new Cell(new Rune('A'), 3);
        var b = new Cell(new Rune('A'), 3);
        var c = new Cell(new Rune('B'), 3);
        var d = new Cell(new Rune('A'), 4);

        Assert.True(a == b);
        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals("not a cell"));
        Assert.True(a != c);
        Assert.True(a != d);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Blank_cell_is_a_default_styled_space()
    {
        Assert.Equal(' ', Cell.Blank.Rune.Value);
        Assert.Equal(0, Cell.Blank.StyleId);
        Assert.False(Cell.Blank.IsWideTrailing);
        Assert.Equal(' ', Cell.Blank.Codepoint);
    }

    [Fact]
    public void Styled_run_equality_considers_text_and_style()
    {
        var a = new StyledRun("hi", TerminalStyle.Default);
        var b = new StyledRun("hi", TerminalStyle.Default);
        var c = new StyledRun("bye", TerminalStyle.Default);
        var d = new StyledRun("hi", new TerminalStyle(AnsiColor.FromPalette(2), AnsiColor.Default, TextStyleFlags.None));

        Assert.Equal("hi", a.Text);
        Assert.Equal(TerminalStyle.Default, a.Style);
        Assert.True(a == b);
        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals("not a run"));
        Assert.True(a != c);
        Assert.True(a != d);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
