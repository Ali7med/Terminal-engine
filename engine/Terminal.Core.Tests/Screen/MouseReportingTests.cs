using Terminal.Core.Screen;

namespace Terminal.Core.Tests.Screen;

public class MouseReportingTests
{
    [Fact]
    public void Disabled_by_default_returns_null()
    {
        var sb = new ScreenBuffer(80, 24);
        Assert.False(sb.MouseReportingEnabled);
        Assert.Null(sb.EncodeMouse(MouseButton.Left, MouseEventType.Press, 0, 0));
    }

    [Fact]
    public void Mode_1000_enables_x10_click_encoding()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString("[?1000h");
        Assert.True(sb.MouseReportingEnabled);
        // X10: ESC [ M Cb Cx Cy — left press at (0,0): Cb=32(space), Cx=Cy=33('!').
        Assert.Equal("[M !!", sb.EncodeMouse(MouseButton.Left, MouseEventType.Press, 0, 0));
    }

    [Fact]
    public void Mode_1000_low_disables()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString("[?1000h");
        Assert.True(sb.MouseReportingEnabled);
        sb.FeedString("[?1000l");
        Assert.False(sb.MouseReportingEnabled);
    }

    [Fact]
    public void Mode_1006_uses_sgr_encoding_press_M_release_m()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString("[?1000h[?1006h");
        Assert.Equal("[<0;1;1M", sb.EncodeMouse(MouseButton.Left, MouseEventType.Press, 0, 0));
        Assert.Equal("[<0;3;2m", sb.EncodeMouse(MouseButton.Left, MouseEventType.Release, 2, 1));
    }

    [Fact]
    public void Mode_1002_reports_drag()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString("[?1002h");
        Assert.True(sb.MouseReportsDrag);
        Assert.False(sb.MouseReportsAllMotion);
    }
}
