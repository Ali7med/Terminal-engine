using System.Text;
using Terminal.Core.Screen;
using Terminal.Core.Vt;

namespace Terminal.Core.Tests.Screen;

public class ScrollbackBufferTests
{
    private static Cell[] Line(char c) => new[] { new Cell(new Rune(c), 0) };

    [Fact]
    public void Add_below_capacity_retains_all_in_order()
    {
        var sb = new ScrollbackBuffer(4);
        Assert.False(sb.Add(Line('a')));
        Assert.False(sb.Add(Line('b')));
        Assert.False(sb.Add(Line('c')));

        Assert.Equal(3, sb.Count);
        Assert.Equal(0, sb.Evicted);
        Assert.Equal('a', sb[0][0].Rune.Value);
        Assert.Equal('b', sb[1][0].Rune.Value);
        Assert.Equal('c', sb[2][0].Rune.Value);
    }

    [Fact]
    public void Add_past_capacity_evicts_oldest_and_keeps_order()
    {
        var sb = new ScrollbackBuffer(3);
        foreach (char c in "abcde")
        {
            bool evicted = sb.Add(Line(c));
            Assert.Equal(c >= 'd', evicted); // 'd' and 'e' evict once full
        }

        Assert.Equal(3, sb.Count);
        Assert.Equal(2, sb.Evicted);
        // oldest two ('a','b') dropped; ring now holds c,d,e in order
        Assert.Equal('c', sb[0][0].Rune.Value);
        Assert.Equal('d', sb[1][0].Rune.Value);
        Assert.Equal('e', sb[2][0].Rune.Value);
    }

    [Fact]
    public void Zero_capacity_retains_nothing_but_counts_evictions()
    {
        var sb = new ScrollbackBuffer(0);
        Assert.True(sb.Add(Line('a')));
        Assert.True(sb.Add(Line('b')));
        Assert.Equal(0, sb.Count);
        Assert.Equal(2, sb.Evicted);
    }

    [Fact]
    public void Clear_drops_lines_and_counts_them_as_evicted()
    {
        var sb = new ScrollbackBuffer(5);
        sb.Add(Line('a'));
        sb.Add(Line('b'));
        sb.Clear();

        Assert.Equal(0, sb.Count);
        Assert.Equal(2, sb.Evicted);
    }

    [Fact]
    public void Indexer_rejects_out_of_range()
    {
        var sb = new ScrollbackBuffer(2);
        sb.Add(Line('a'));
        Assert.Throws<ArgumentOutOfRangeException>(() => sb[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => sb[-1]);
    }
}
