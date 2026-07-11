using System.Linq;
using Terminal.Core.Screen;

namespace Terminal.Core.Tests.Screen;

public class HyperlinkTests
{
    private const string Esc = "";   // OSC/CSI introducer
    private const string Bel = "";   // string terminator (BEL)

    // OSC 8 hyperlink: ESC ] 8 ; params ; URI BEL   (empty URI ends the link).
    private static string Osc8(string uri) => Esc + "]8;;" + uri + Bel;
    private static readonly string Osc8End = Esc + "]8;;" + Bel;

    private static StyledRun[] FirstNonEmptyRow(ScreenBuffer sb)
        => sb.Snapshot().Lines.First(l => l.Length > 0);

    [Fact]
    public void Text_without_osc8_has_no_link()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString("plain text");

        var runs = FirstNonEmptyRow(sb);
        Assert.All(runs, r => Assert.Null(r.Link));
    }

    [Fact]
    public void Osc8_attaches_uri_to_the_enclosed_run()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString(Osc8("https://example.com") + "click" + Osc8End);

        var runs = FirstNonEmptyRow(sb);
        Assert.Single(runs);
        Assert.Equal("click", runs[0].Text);
        Assert.Equal("https://example.com", runs[0].Link);
    }

    [Fact]
    public void Empty_uri_ends_the_hyperlink()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString(Osc8("https://a.example") + "link" + Osc8End + "tail");

        var runs = FirstNonEmptyRow(sb);
        Assert.Equal(2, runs.Length);
        Assert.Equal("link", runs[0].Text);
        Assert.Equal("https://a.example", runs[0].Link);
        Assert.Equal("tail", runs[1].Text);
        Assert.Null(runs[1].Link);
    }

    [Fact]
    public void Adjacent_different_links_split_into_separate_runs()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString(Osc8("https://one") + "a" + Osc8End
                    + Osc8("https://two") + "b" + Osc8End);

        var runs = FirstNonEmptyRow(sb);
        Assert.Equal(2, runs.Length);
        Assert.Equal("https://one", runs[0].Link);
        Assert.Equal("https://two", runs[1].Link);
    }

    [Fact]
    public void Params_field_before_uri_is_skipped()
    {
        var sb = new ScreenBuffer(80, 24);
        // ESC ] 8 ; id=42 ; URI BEL — the params field must not leak into the URI.
        sb.FeedString(Esc + "]8;id=42;https://withparams" + Bel + "x" + Osc8End);

        var runs = FirstNonEmptyRow(sb);
        Assert.Equal("https://withparams", runs[0].Link);
    }

    [Fact]
    public void Link_is_interned_on_the_cell()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString(Osc8("https://cell") + "Z" + Osc8End);

        var cell = sb.GetCell(0, 0);
        Assert.NotEqual(0, cell.LinkId);
        Assert.Equal("https://cell", sb.Links.Resolve(cell.LinkId));
    }

    [Fact]
    public void Same_uri_reuses_one_link_id()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString(Osc8("https://dup") + "a" + Osc8End
                    + Osc8("https://dup") + "b" + Osc8End);

        // id 0 is "no link"; the two occurrences of one URI must share a single slot.
        Assert.Equal(2, sb.Links.Count);
    }

    [Fact]
    public void Reset_clears_the_active_hyperlink()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString(Osc8("https://before")); // open a link ...
        sb.FeedString(Esc + "c");               // ... then RIS (hard reset)
        sb.FeedString("after");

        var runs = FirstNonEmptyRow(sb);
        Assert.All(runs, r => Assert.Null(r.Link));
    }

    [Fact]
    public void Erased_blank_cells_carry_no_link()
    {
        var sb = new ScreenBuffer(80, 24);
        // Open a link and print, then erase the whole line (EL 2) — blanks must not inherit the link.
        sb.FeedString(Osc8("https://gone") + "text");
        sb.FeedString(Esc + "[2K");

        Assert.Equal(0, sb.GetCell(0, 0).LinkId);
    }
}
