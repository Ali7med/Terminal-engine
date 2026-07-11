using System.Text;
using Terminal.Core.Vt;

namespace Terminal.Core.Tests.Vt;

/// <summary>
/// End-to-end style recorder: runs the parser + SGR processor together and captures the
/// visible text broken into styled runs — the pipeline a renderer/buffer would consume.
/// </summary>
internal sealed class StyledTextRecorder : IVtParserSink
{
    private TerminalStyle _style = TerminalStyle.Default;
    private readonly StringBuilder _current = new();
    public readonly List<(string Text, TerminalStyle Style)> Runs = new();

    private void Flush()
    {
        if (_current.Length > 0)
        {
            Runs.Add((_current.ToString(), _style));
            _current.Clear();
        }
    }

    public void Print(Rune rune) => _current.Append(rune.ToString());
    public void Execute(byte control) { }
    public void EscDispatch(char finalByte, char intermediate) { }

    public void CsiDispatch(char finalByte, VtParams parameters, char privateMarker, char intermediate)
    {
        if (finalByte == 'm' && privateMarker == '\0')
        {
            Flush();
            _style = SgrProcessor.Apply(_style, parameters);
        }
    }

    public void OscDispatch(string data) { }

    public IReadOnlyList<(string Text, TerminalStyle Style)> Finish()
    {
        Flush();
        return Runs;
    }
}

public class VtIntegrationTests
{
    private const char ESC = (char)0x1B;

    private static IReadOnlyList<(string Text, TerminalStyle Style)> Render(string s)
    {
        var recorder = new StyledTextRecorder();
        new VtParser(recorder).Feed(Encoding.UTF8.GetBytes(s));
        return recorder.Finish();
    }

    [Fact]
    public void Colored_spans_are_split_by_style()
    {
        var runs = Render(ESC + "[1;31mERROR" + ESC + "[0m: " + ESC + "[32mok" + ESC + "[0m");

        Assert.Equal(3, runs.Count);

        Assert.Equal("ERROR", runs[0].Text);
        Assert.True(runs[0].Style.Has(TextStyleFlags.Bold));
        Assert.Equal(AnsiColor.FromPalette(1), runs[0].Style.Foreground);

        Assert.Equal(": ", runs[1].Text);
        Assert.Equal(TerminalStyle.Default, runs[1].Style);

        Assert.Equal("ok", runs[2].Text);
        Assert.Equal(AnsiColor.FromPalette(2), runs[2].Style.Foreground);
    }

    [Fact]
    public void GitLog_style_line_parses_visible_text_and_colors()
    {
        // Mimics `git log --oneline --color`: yellow hash, default text, then reset.
        var runs = Render(ESC + "[33m3a1f9c2" + ESC + "[m Initial commit");

        Assert.Equal("3a1f9c2", runs[0].Text);
        Assert.Equal(AnsiColor.FromPalette(3), runs[0].Style.Foreground);

        Assert.Equal(" Initial commit", runs[1].Text);
        Assert.Equal(TerminalStyle.Default, runs[1].Style);

        // The concatenated visible text is exactly the payload with escape codes stripped.
        var visible = string.Concat(runs.Select(r => r.Text));
        Assert.Equal("3a1f9c2 Initial commit", visible);
    }

    [Fact]
    public void Truecolor_span_carries_rgb()
    {
        var runs = Render(ESC + "[38;2;255;128;0mHi" + ESC + "[0m");
        Assert.Single(runs);
        Assert.Equal(AnsiColor.ColorKind.Rgb, runs[0].Style.Foreground.Kind);
        Assert.Equal(255, runs[0].Style.Foreground.R);
        Assert.Equal(128, runs[0].Style.Foreground.G);
        Assert.Equal(0, runs[0].Style.Foreground.B);
    }
}
