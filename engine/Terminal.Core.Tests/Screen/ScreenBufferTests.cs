using System.Text;
using Terminal.Core.Screen;
using Terminal.Core.Vt;

namespace Terminal.Core.Tests.Screen;

public class ScreenBufferTests
{
    private const char ESC = (char)0x1B;

    private static string Csi(string body) => ESC + "[" + body;

    // ===== printing & wrapping =====

    [Fact]
    public void Prints_text_at_origin()
    {
        var b = new ScreenBuffer(80, 24);
        b.FeedString("hello");
        Assert.Equal("hello", b.GetRowText(0).TrimEnd());
        Assert.Equal(0, b.CursorRow);
        Assert.Equal(5, b.CursorCol);
    }

    [Fact]
    public void Cr_lf_moves_to_next_row()
    {
        var b = new ScreenBuffer(80, 24);
        b.FeedString("a\r\nb");
        Assert.Equal("a", b.GetRowText(0).TrimEnd());
        Assert.Equal("b", b.GetRowText(1).TrimEnd());
    }

    [Fact]
    public void Deferred_wrap_pushes_overflow_to_next_line()
    {
        var b = new ScreenBuffer(3, 24);
        b.FeedString("abcd");
        Assert.Equal("abc", b.GetRowText(0).TrimEnd());
        Assert.Equal("d", b.GetRowText(1).TrimEnd());
        Assert.Equal(1, b.CursorRow);
        Assert.Equal(1, b.CursorCol);
    }

    [Fact]
    public void Autowrap_off_overwrites_last_column()
    {
        var b = new ScreenBuffer(3, 24);
        b.FeedString(Csi("?7l")); // DECAWM off
        Assert.False(b.AutoWrap);
        b.FeedString("abcd");
        Assert.Equal("abd", b.GetRowText(0).TrimEnd());
        Assert.Equal(0, b.CursorRow);
    }

    [Fact]
    public void Backspace_and_tab_move_cursor()
    {
        var b = new ScreenBuffer(80, 24);
        b.FeedString("ab\b");
        Assert.Equal(1, b.CursorCol);
        b.FeedString("\t");
        Assert.Equal(8, b.CursorCol); // next tab stop
    }

    // ===== cursor positioning =====

    [Fact]
    public void Cup_moves_cursor_one_based()
    {
        var b = new ScreenBuffer(80, 24);
        b.FeedString(Csi("5;10H"));
        Assert.Equal(4, b.CursorRow);
        Assert.Equal(9, b.CursorCol);
        b.FeedString("X");
        Assert.Equal('X', b.GetCell(4, 9).Rune.Value);
    }

    [Fact]
    public void Relative_cursor_moves_clamp_to_grid()
    {
        var b = new ScreenBuffer(80, 24);
        b.FeedString(Csi("10;10H"));
        b.FeedString(Csi("3A")); // up 3
        Assert.Equal(6, b.CursorRow);
        b.FeedString(Csi("100B")); // down, clamps to last row
        Assert.Equal(23, b.CursorRow);
        b.FeedString(Csi("5D")); // left 5
        Assert.Equal(4, b.CursorCol);
    }

    [Fact]
    public void Save_and_restore_cursor_decsc_decrc()
    {
        var b = new ScreenBuffer(80, 24);
        b.FeedString(Csi("3;4H"));
        b.FeedString(ESC + "7");   // DECSC
        b.FeedString(Csi("10;20H"));
        b.FeedString(ESC + "8");   // DECRC
        Assert.Equal(2, b.CursorRow);
        Assert.Equal(3, b.CursorCol);
    }

    // ===== erase =====

    [Fact]
    public void Erase_line_to_end()
    {
        var b = new ScreenBuffer(10, 24);
        b.FeedString("abcdef");
        b.FeedString(Csi("1;4H")); // col index 3
        b.FeedString(Csi("0K"));   // EL 0
        Assert.Equal("abc", b.GetRowText(0).TrimEnd());
    }

    [Fact]
    public void Erase_display_all_clears_grid()
    {
        var b = new ScreenBuffer(10, 4);
        b.FeedString("row0\r\nrow1\r\nrow2");
        b.FeedString(Csi("2J")); // ED 2
        for (int r = 0; r < 4; r++)
            Assert.Equal(string.Empty, b.GetRowText(r).TrimEnd());
    }

    [Fact]
    public void Erase_display_mode3_clears_scrollback()
    {
        var b = new ScreenBuffer(10, 2, scrollbackCapacity: 100);
        b.FeedString("a\r\nb\r\nc\r\nd\r\n"); // pushes lines into scrollback
        Assert.True(b.ScrollbackCount > 0);
        b.FeedString(Csi("3J"));
        Assert.Equal(0, b.ScrollbackCount);
    }

    // ===== insert / delete =====

    [Fact]
    public void Insert_chars_shifts_right()
    {
        var b = new ScreenBuffer(6, 24);
        b.FeedString("abcdef");
        b.FeedString(Csi("1;1H"));
        b.FeedString(Csi("2@")); // ICH 2
        Assert.Equal("  abcd", b.GetRowText(0));
    }

    [Fact]
    public void Delete_chars_shifts_left()
    {
        var b = new ScreenBuffer(6, 24);
        b.FeedString("abcdef");
        b.FeedString(Csi("1;1H"));
        b.FeedString(Csi("2P")); // DCH 2
        Assert.Equal("cdef", b.GetRowText(0).TrimEnd());
    }

    [Fact]
    public void Insert_lines_pushes_rows_down_within_region()
    {
        var b = new ScreenBuffer(10, 4);
        b.FeedString("r0\r\nr1\r\nr2\r\nr3");
        b.FeedString(Csi("2;1H")); // row index 1
        b.FeedString(Csi("1L"));   // IL 1
        Assert.Equal("r0", b.GetRowText(0).TrimEnd());
        Assert.Equal(string.Empty, b.GetRowText(1).TrimEnd());
        Assert.Equal("r1", b.GetRowText(2).TrimEnd());
        Assert.Equal("r2", b.GetRowText(3).TrimEnd());
    }

    [Fact]
    public void Delete_lines_pulls_rows_up_within_region()
    {
        var b = new ScreenBuffer(10, 4);
        b.FeedString("r0\r\nr1\r\nr2\r\nr3");
        b.FeedString(Csi("2;1H")); // row index 1
        b.FeedString(Csi("1M"));   // DL 1
        Assert.Equal("r0", b.GetRowText(0).TrimEnd());
        Assert.Equal("r2", b.GetRowText(1).TrimEnd());
        Assert.Equal("r3", b.GetRowText(2).TrimEnd());
        Assert.Equal(string.Empty, b.GetRowText(3).TrimEnd());
    }

    // ===== scroll region =====

    [Fact]
    public void Scroll_region_confines_line_feed_scrolling()
    {
        var b = new ScreenBuffer(10, 5);
        b.FeedString(Csi("2;4r")); // DECSTBM rows 2..4 (index 1..3), homes cursor
        // Fill the region then force it to scroll once.
        b.FeedString(Csi("2;1H") + "A\r\n" + "B\r\n" + "C\r\n" + "D");
        Assert.Equal(string.Empty, b.GetRowText(0).TrimEnd()); // row 0 untouched (above region)
        Assert.Equal("B", b.GetRowText(1).TrimEnd());
        Assert.Equal("C", b.GetRowText(2).TrimEnd());
        Assert.Equal("D", b.GetRowText(3).TrimEnd());
        Assert.Equal(string.Empty, b.GetRowText(4).TrimEnd()); // row 4 untouched (below region)
        Assert.Equal(0, b.ScrollbackCount); // region scroll never feeds scrollback
    }

    // ===== scrollback =====

    [Fact]
    public void Line_feed_at_bottom_feeds_scrollback_in_order()
    {
        var b = new ScreenBuffer(10, 2, scrollbackCapacity: 100);
        b.FeedString("A\r\nB\r\nC\r\n");
        Assert.Equal(2, b.ScrollbackCount);
        Assert.Equal("A", b.GetScrollbackText(0).TrimEnd());
        Assert.Equal("B", b.GetScrollbackText(1).TrimEnd());
        Assert.Equal("C", b.GetRowText(0).TrimEnd());
    }

    [Fact]
    public void Scrollback_is_capped_at_capacity()
    {
        var b = new ScreenBuffer(5, 2, scrollbackCapacity: 3);
        var sb = new StringBuilder();
        for (int i = 0; i < 20; i++)
            sb.Append(i).Append("\r\n");
        b.FeedString(sb.ToString());
        Assert.Equal(3, b.ScrollbackCount);
    }

    [Fact]
    public void Feeding_a_million_lines_keeps_scrollback_capped()
    {
        // Guide T-004.6: memory must stay bounded under continuous output. The scrollback ring
        // retains at most `capacity` lines regardless of how much streams through.
        var b = new ScreenBuffer(80, 24, scrollbackCapacity: 10_000);
        var chunk = Encoding.UTF8.GetBytes(new string('\n', 1_000));
        for (int i = 0; i < 1_000; i++) // 1,000,000 line feeds
            b.Feed(chunk);
        Assert.Equal(10_000, b.ScrollbackCount);
    }

    // ===== alternate screen =====

    [Fact]
    public void Alt_screen_hides_main_and_restores_on_exit()
    {
        var b = new ScreenBuffer(80, 24);
        b.FeedString("main");
        Assert.Equal(4, b.CursorCol);

        b.FeedString(Csi("?1049h")); // enter alt, save cursor
        Assert.True(b.IsAltScreen);
        Assert.Equal(0, b.CursorRow);
        Assert.Equal(0, b.CursorCol);
        b.FeedString("ALT");
        Assert.Equal("ALT", b.GetRowText(0).TrimEnd());

        b.FeedString(Csi("?1049l")); // leave alt, restore cursor
        Assert.False(b.IsAltScreen);
        Assert.Equal("main", b.GetRowText(0).TrimEnd()); // main content back
        Assert.Equal(4, b.CursorCol);                     // cursor restored
    }

    [Fact]
    public void Alt_screen_keeps_no_scrollback()
    {
        var b = new ScreenBuffer(10, 2, scrollbackCapacity: 100);
        b.FeedString(Csi("?1049h"));
        b.FeedString("a\r\nb\r\nc\r\nd\r\ne\r\n"); // lots of scrolling within alt
        Assert.Equal(0, b.ScrollbackCount);
    }

    // ===== resize =====

    [Fact]
    public void Resize_grow_preserves_scrollback()
    {
        var b = new ScreenBuffer(10, 2, scrollbackCapacity: 100);
        b.FeedString("A\r\nB\r\nC\r\n"); // scrollback: A,B
        Assert.Equal(2, b.ScrollbackCount);

        b.Resize(10, 5);
        Assert.Equal(2, b.ScrollbackCount);
        Assert.Equal("A", b.GetScrollbackText(0).TrimEnd());
        Assert.Equal("B", b.GetScrollbackText(1).TrimEnd());
    }

    [Fact]
    public void Resize_shrink_pushes_overflow_rows_to_scrollback()
    {
        var b = new ScreenBuffer(10, 3, scrollbackCapacity: 100);
        b.FeedString("L0\r\nL1\r\nL2"); // fills three rows, no scroll yet
        Assert.Equal(0, b.ScrollbackCount);

        b.Resize(10, 2); // one row overflows off the top
        Assert.Equal(1, b.ScrollbackCount);
        Assert.Equal("L0", b.GetScrollbackText(0).TrimEnd());
        Assert.Equal("L1", b.GetRowText(0).TrimEnd());
        Assert.Equal("L2", b.GetRowText(1).TrimEnd());
    }

    // ===== SGR styling on cells =====

    [Fact]
    public void Sgr_colours_are_recorded_per_cell()
    {
        var b = new ScreenBuffer(80, 24);
        b.FeedString(Csi("31m") + "A" + Csi("0m") + "B");
        Assert.Equal(AnsiColor.FromPalette(1), b.StyleAt(0, 0).Foreground);
        Assert.Equal(AnsiColor.Default, b.StyleAt(0, 1).Foreground);
    }

    [Fact]
    public void Truecolor_sgr_is_recorded()
    {
        var b = new ScreenBuffer(80, 24);
        b.FeedString(Csi("38;2;10;20;30m") + "X");
        Assert.Equal(AnsiColor.FromRgb(10, 20, 30), b.StyleAt(0, 0).Foreground);
    }

    // ===== cursor visibility =====

    [Fact]
    public void Cursor_visibility_toggles_with_dectcem()
    {
        var b = new ScreenBuffer(80, 24);
        Assert.True(b.CursorVisible);
        b.FeedString(Csi("?25l"));
        Assert.False(b.CursorVisible);
        b.FeedString(Csi("?25h"));
        Assert.True(b.CursorVisible);
    }

    // ===== wide characters =====

    [Fact]
    public void Wide_char_occupies_two_columns()
    {
        var b = new ScreenBuffer(5, 24);
        b.FeedString("世");
        Assert.Equal("世", b.GetCell(0, 0).Rune.ToString());
        Assert.True(b.GetCell(0, 1).IsWideTrailing);
        Assert.Equal(2, b.CursorCol);
    }

    [Fact]
    public void Wide_char_wraps_when_it_cannot_fit()
    {
        var b = new ScreenBuffer(2, 24);
        b.FeedString("a世"); // 'a' in col0, '世' cannot fit the last single column
        Assert.Equal("a", b.GetRowText(0).TrimEnd());
        Assert.Equal("世", b.GetCell(1, 0).Rune.ToString());
        Assert.True(b.GetCell(1, 1).IsWideTrailing);
    }

    // ===== title (OSC) =====

    [Fact]
    public void Osc_sets_window_title()
    {
        var b = new ScreenBuffer(80, 24);
        b.FeedString(ESC + "]0;my title" + (char)0x07);
        Assert.Equal("my title", b.Title);
    }

    // ===== dirty tracking =====

    [Fact]
    public void Snapshot_resets_dirty_tracking()
    {
        var b = new ScreenBuffer(80, 24);
        b.Snapshot(); // consumes the initial full-paint dirty state
        Assert.Equal(long.MaxValue, b.DirtyFromLine);

        b.FeedString(Csi("3;1H") + "X"); // write on visible row index 2
        Assert.Equal(2, b.DirtyFromLine);

        var snap = b.Snapshot();
        Assert.Equal(2, snap.DirtyFromLine);
        Assert.Equal(long.MaxValue, b.DirtyFromLine);
    }

    [Fact]
    public void Snapshot_lines_include_scrollback_then_visible()
    {
        var b = new ScreenBuffer(10, 2, scrollbackCapacity: 100);
        b.FeedString("A\r\nB\r\nC");
        var snap = b.Snapshot();
        Assert.False(snap.AltScreen);
        Assert.Equal(1, b.ScrollbackCount);
        Assert.Equal(b.ScrollbackCount + snap.Rows, snap.Lines.Count);
        Assert.Equal("A", RunText(snap.Lines[0]));                    // oldest scrollback line
        Assert.Equal("B", RunText(snap.Lines[snap.Lines.Count - 2])); // first visible row
        Assert.Equal("C", RunText(snap.Lines[snap.Lines.Count - 1])); // last visible row
    }

    // ===== robustness =====

    [Fact]
    public void Random_bytes_never_throw()
    {
        var b = new ScreenBuffer(80, 24);
        var bytes = new byte[4096];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((i * 131 + 7) & 0xFF);
        var ex = Record.Exception(() => b.Feed(bytes));
        Assert.Null(ex);
    }

    private static string RunText(StyledRun[] runs)
    {
        var sb = new StringBuilder();
        foreach (var run in runs)
            sb.Append(run.Text);
        return sb.ToString();
    }
}
