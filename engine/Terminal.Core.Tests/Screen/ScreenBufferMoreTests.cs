using Terminal.Core.Screen;
using Terminal.Core.Vt;

namespace Terminal.Core.Tests.Screen;

/// <summary>Additional coverage for the less common control paths of <see cref="ScreenBuffer"/>.</summary>
public class ScreenBufferMoreTests
{
    private const char ESC = (char)0x1B;

    private static string Csi(string body) => ESC + "[" + body;

    [Fact]
    public void Index_and_next_line_and_reverse_index()
    {
        var b = new ScreenBuffer(10, 4);
        b.FeedString("A" + ESC + "D");        // 'A' then IND (down, column kept)
        Assert.Equal(1, b.CursorRow);
        Assert.Equal(1, b.CursorCol);

        b.FeedString(ESC + "E");              // NEL (down + carriage return)
        Assert.Equal(2, b.CursorRow);
        Assert.Equal(0, b.CursorCol);

        b.FeedString(ESC + "M");              // RI (up)
        Assert.Equal(1, b.CursorRow);
    }

    [Fact]
    public void Reverse_index_at_top_scrolls_region_down()
    {
        var b = new ScreenBuffer(10, 3);
        b.FeedString("r0\r\nr1\r\nr2");
        b.FeedString(Csi("1;1H"));            // home
        b.FeedString(ESC + "M");              // RI at top → scroll down, opening a blank top row
        Assert.Equal(string.Empty, b.GetRowText(0).TrimEnd());
        Assert.Equal("r0", b.GetRowText(1).TrimEnd());
        Assert.Equal("r1", b.GetRowText(2).TrimEnd());
    }

    [Fact]
    public void Vertical_tab_and_form_feed_act_as_line_feed()
    {
        var b = new ScreenBuffer(10, 5);
        b.FeedString("a\vb\fc");
        Assert.Equal(2, b.CursorRow);
    }

    [Fact]
    public void Cnl_and_cpl_move_and_reset_column()
    {
        var b = new ScreenBuffer(10, 10);
        b.FeedString(Csi("5;5H"));
        b.FeedString(Csi("2E"));  // CNL: down 2, col 0
        Assert.Equal(6, b.CursorRow);
        Assert.Equal(0, b.CursorCol);
        b.FeedString(Csi("3;3H"));
        b.FeedString(Csi("1F"));  // CPL: up 1, col 0
        Assert.Equal(1, b.CursorRow);
        Assert.Equal(0, b.CursorCol);
    }

    [Fact]
    public void Cha_and_vpa_absolute_column_and_row()
    {
        var b = new ScreenBuffer(20, 10);
        b.FeedString(Csi("6G"));  // CHA column 6 (index 5)
        Assert.Equal(5, b.CursorCol);
        b.FeedString(Csi("4d"));  // VPA row 4 (index 3)
        Assert.Equal(3, b.CursorRow);
    }

    [Fact]
    public void Erase_line_to_start()
    {
        var b = new ScreenBuffer(6, 4);
        b.FeedString("abcdef");
        b.FeedString(Csi("1;4H")); // col index 3
        b.FeedString(Csi("1K"));   // EL 1: erase start..cursor inclusive
        Assert.Equal("    ef", b.GetRowText(0));
    }

    [Fact]
    public void Erase_display_to_cursor()
    {
        var b = new ScreenBuffer(6, 3);
        b.FeedString("r0\r\nr1\r\nr2");
        b.FeedString(Csi("2;3H")); // row index 1, col index 2 (past "r1")
        b.FeedString(Csi("1J"));   // ED 1: erase start of screen..cursor inclusive
        Assert.Equal(string.Empty, b.GetRowText(0).TrimEnd());
        Assert.Equal(string.Empty, b.GetRowText(1).TrimEnd()); // whole row cleared through cursor
        Assert.Equal("r2", b.GetRowText(2).TrimEnd());
    }

    [Fact]
    public void Erase_chars_blanks_in_place()
    {
        var b = new ScreenBuffer(6, 3);
        b.FeedString("abcdef");
        b.FeedString(Csi("1;1H"));
        b.FeedString(Csi("3X"));   // ECH 3
        Assert.Equal("   def", b.GetRowText(0));
    }

    [Fact]
    public void Unknown_erase_modes_are_ignored()
    {
        var b = new ScreenBuffer(6, 3);
        b.FeedString("abcdef");
        b.FeedString(Csi("9K")); // invalid EL mode
        b.FeedString(Csi("9J")); // invalid ED mode
        Assert.Equal("abcdef", b.GetRowText(0).TrimEnd());
    }

    [Fact]
    public void Scroll_up_and_down_via_su_sd()
    {
        var b = new ScreenBuffer(10, 3, scrollbackCapacity: 100);
        b.FeedString("r0\r\nr1\r\nr2");
        b.FeedString(Csi("1S")); // SU 1: whole screen up, top row to scrollback
        Assert.Equal("r1", b.GetRowText(0).TrimEnd());
        Assert.Equal("r2", b.GetRowText(1).TrimEnd());
        Assert.Equal(string.Empty, b.GetRowText(2).TrimEnd());
        Assert.Equal(1, b.ScrollbackCount);

        b.FeedString(Csi("1T")); // SD 1: whole screen down, blank top row
        Assert.Equal(string.Empty, b.GetRowText(0).TrimEnd());
        Assert.Equal("r1", b.GetRowText(1).TrimEnd());
    }

    [Fact]
    public void Application_cursor_keys_and_bracketed_paste_flags()
    {
        var b = new ScreenBuffer(10, 3);
        Assert.False(b.ApplicationCursorKeys);
        Assert.False(b.BracketedPaste);

        b.FeedString(Csi("?1h") + Csi("?2004h"));
        Assert.True(b.ApplicationCursorKeys);
        Assert.True(b.BracketedPaste);

        b.FeedString(Csi("?1l") + Csi("?2004l"));
        Assert.False(b.ApplicationCursorKeys);
        Assert.False(b.BracketedPaste);
    }

    [Fact]
    public void Decsc_restores_style_as_well_as_position()
    {
        var b = new ScreenBuffer(10, 3);
        b.FeedString(Csi("31m"));   // red
        b.FeedString(ESC + "7");    // DECSC
        b.FeedString(Csi("0m"));    // back to default
        b.FeedString(ESC + "8");    // DECRC — restores red pen too
        b.FeedString("X");
        Assert.Equal(AnsiColor.FromPalette(1), b.StyleAt(0, 0).Foreground);
    }

    [Fact]
    public void Clear_and_ris_reset_everything()
    {
        var b = new ScreenBuffer(10, 2, scrollbackCapacity: 100);
        b.FeedString(ESC + "]0;t" + (char)0x07);
        b.FeedString("a\r\nb\r\nc\r\n" + Csi("31m") + Csi("?25l"));
        Assert.True(b.ScrollbackCount > 0);

        b.Clear();
        Assert.Equal(0, b.ScrollbackCount);
        Assert.Equal(0, b.CursorRow);
        Assert.Equal(0, b.CursorCol);
        Assert.True(b.CursorVisible);
        Assert.Equal(string.Empty, b.Title);
        Assert.Equal(string.Empty, b.GetRowText(0).TrimEnd());

        // RIS via ESC c takes the same path.
        b.FeedString("live" + ESC + "c");
        Assert.Equal(string.Empty, b.GetRowText(0).TrimEnd());
    }

    [Fact]
    public void Osc_two_also_sets_title()
    {
        var b = new ScreenBuffer(10, 2);
        b.FeedString(ESC + "]2;win" + (char)0x07);
        Assert.Equal("win", b.Title);
    }

    [Fact]
    public void Invalid_scroll_region_resets_to_full_screen()
    {
        var b = new ScreenBuffer(10, 3, scrollbackCapacity: 100);
        b.FeedString(Csi("3;2r")); // top > bottom → invalid → full-screen region
        b.FeedString("a\r\nb\r\nc\r\nd\r\n");
        Assert.True(b.ScrollbackCount > 0); // full-screen scrolling feeds scrollback
    }

    [Fact]
    public void Resize_to_same_size_is_a_noop()
    {
        var b = new ScreenBuffer(10, 3);
        b.FeedString("hi");
        b.Resize(10, 3);
        Assert.Equal(10, b.Cols);
        Assert.Equal(3, b.Rows);
        Assert.Equal("hi", b.GetRowText(0).TrimEnd());
    }

    [Fact]
    public void Wide_char_with_autowrap_off_overwrites_from_penultimate_column()
    {
        var b = new ScreenBuffer(2, 3);
        b.FeedString(Csi("?7l")); // autowrap off
        b.FeedString("a世");       // '世' cannot start at the last column; falls back to cols-2
        Assert.Equal("世", b.GetCell(0, 0).Rune.ToString());
        Assert.True(b.GetCell(0, 1).IsWideTrailing);
        Assert.Equal(0, b.CursorRow); // no wrap
    }

    [Fact]
    public void Wide_trailing_cell_reports_zero_codepoint()
    {
        var b = new ScreenBuffer(5, 2);
        b.FeedString("世");
        var trailing = b.GetCell(0, 1);
        Assert.True(trailing.IsWideTrailing);
        Assert.Equal(0, trailing.Codepoint);
        Assert.Equal(0, trailing.Rune.Value);
    }

    [Fact]
    public void Feed_string_rejects_null()
    {
        var b = new ScreenBuffer(10, 3);
        Assert.Throws<ArgumentNullException>(() => b.FeedString(null!));
    }

    [Fact]
    public void Cell_and_row_accessors_reject_out_of_range()
    {
        var b = new ScreenBuffer(10, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => b.GetCell(3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.GetCell(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.GetRowText(3));
    }

    [Fact]
    public void Alt_screen_snapshot_shows_visible_only()
    {
        var b = new ScreenBuffer(10, 2, scrollbackCapacity: 100);
        b.FeedString("A\r\nB\r\nC");      // builds scrollback on the main screen
        b.FeedString(Csi("?1049h"));      // switch to alt
        b.FeedString("Z");
        var snap = b.Snapshot();
        Assert.True(snap.AltScreen);
        Assert.Equal(snap.Rows, snap.Lines.Count); // no scrollback in the alt snapshot
        Assert.Equal(b.GridTopLine, snap.BaseLine);
    }
}
