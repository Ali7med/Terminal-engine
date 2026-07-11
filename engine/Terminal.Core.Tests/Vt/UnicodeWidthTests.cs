using System.Text;
using Terminal.Core.Vt;

namespace Terminal.Core.Tests.Vt;

public class UnicodeWidthTests
{
    private static int Width(string s) => UnicodeWidth.Measure(Rune.GetRuneAt(s, 0));

    [Fact]
    public void Ascii_is_width_one()
    {
        Assert.Equal(1, Width("A"));
        Assert.Equal(1, Width("9"));
    }

    [Fact]
    public void Arabic_letters_are_width_one()
    {
        // Right-to-left shaping is a rendering concern; the cell width is still 1.
        Assert.Equal(1, Width("ب"));
    }

    [Fact]
    public void Cjk_is_width_two()
    {
        Assert.Equal(2, Width("中"));
        Assert.Equal(2, Width("한"));
        Assert.Equal(2, Width("あ"));
    }

    [Fact]
    public void Emoji_is_width_two()
    {
        Assert.Equal(2, UnicodeWidth.Measure(new Rune(0x1F600))); // 😀
    }

    [Fact]
    public void Combining_and_zero_width_are_width_zero()
    {
        Assert.Equal(0, UnicodeWidth.Measure(new Rune(0x0301))); // combining acute accent
        Assert.Equal(0, UnicodeWidth.Measure(new Rune(0x200B))); // zero-width space
        Assert.Equal(0, UnicodeWidth.Measure(new Rune(0x200D))); // zero-width joiner
    }

    [Fact]
    public void Control_characters_are_width_zero()
    {
        Assert.Equal(0, UnicodeWidth.Measure(new Rune(0x07)));   // BEL
        Assert.Equal(0, UnicodeWidth.Measure(new Rune(0x1B)));   // ESC
    }
}
