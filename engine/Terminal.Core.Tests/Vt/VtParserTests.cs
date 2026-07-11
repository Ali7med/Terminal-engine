using System.Text;
using Terminal.Core.Vt;

namespace Terminal.Core.Tests.Vt;

/// <summary>Records the structural events the parser emits, for assertion.</summary>
internal sealed class RecordingSink : IVtParserSink
{
    public sealed record Csi(char Final, int[] Parameters, char Private, char Intermediate);

    public readonly StringBuilder Printed = new();
    public readonly List<byte> Executed = new();
    public readonly List<(char Final, char Intermediate)> Escapes = new();
    public readonly List<Csi> Csis = new();
    public readonly List<string> Oscs = new();

    public void Print(Rune rune) => Printed.Append(rune.ToString());
    public void Execute(byte control) => Executed.Add(control);
    public void EscDispatch(char finalByte, char intermediate) => Escapes.Add((finalByte, intermediate));

    public void CsiDispatch(char finalByte, VtParams parameters, char privateMarker, char intermediate)
    {
        var values = new int[parameters.Count];
        for (int i = 0; i < parameters.Count; i++)
            values[i] = parameters.Get(i, -1);
        Csis.Add(new Csi(finalByte, values, privateMarker, intermediate));
    }

    public void OscDispatch(string data) => Oscs.Add(data);
}

public class VtParserTests
{
    // Control characters built from code points so no raw control byte appears in source.
    private const char ESC = (char)0x1B;
    private const char CAN = (char)0x18;
    private const char BEL = (char)0x07;
    private const char BS = (char)0x08;
    private const char HT = (char)0x09;
    private const char LF = (char)0x0A;
    private const char CR = (char)0x0D;
    private static readonly string ST = new(new[] { ESC, (char)0x5C }); // ESC + backslash

    private static RecordingSink Run(string s)
    {
        var sink = new RecordingSink();
        new VtParser(sink).Feed(Encoding.UTF8.GetBytes(s));
        return sink;
    }

    [Fact]
    public void Prints_plain_ascii()
    {
        var s = Run("hello world");
        Assert.Equal("hello world", s.Printed.ToString());
        Assert.Empty(s.Csis);
    }

    [Fact]
    public void Decodes_utf8_split_across_feeds()
    {
        // Arabic plus an emoji, fed one byte at a time to force split multibyte handling.
        byte[] bytes = Encoding.UTF8.GetBytes("سلام😀");
        var sink = new RecordingSink();
        var parser = new VtParser(sink);
        foreach (byte b in bytes)
            parser.Feed(b);
        Assert.Equal("سلام😀", sink.Printed.ToString());
    }

    [Fact]
    public void C0_controls_are_executed()
    {
        var s = Run("a" + BEL + BS + HT + LF + CR + "b");
        Assert.Equal("ab", s.Printed.ToString());
        Assert.Equal(new byte[] { 0x07, 0x08, 0x09, 0x0A, 0x0D }, s.Executed.ToArray());
    }

    [Fact]
    public void Esc_dispatch_simple_and_with_intermediate()
    {
        var s = Run(ESC + "7" + ESC + "8" + ESC + "M" + ESC + "c" + ESC + "(B");
        Assert.Equal(('7', '\0'), s.Escapes[0]); // DECSC
        Assert.Equal(('8', '\0'), s.Escapes[1]); // DECRC
        Assert.Equal(('M', '\0'), s.Escapes[2]); // RI
        Assert.Equal(('c', '\0'), s.Escapes[3]); // RIS
        Assert.Equal(('B', '('), s.Escapes[4]);  // charset G0 = ASCII
    }

    [Fact]
    public void Csi_final_and_parameters()
    {
        var s = Run(ESC + "[31m");
        Assert.Single(s.Csis);
        Assert.Equal('m', s.Csis[0].Final);
        Assert.Equal(new[] { 31 }, s.Csis[0].Parameters);
        Assert.Equal('\0', s.Csis[0].Private);
    }

    [Fact]
    public void Csi_cursor_position_multiple_params()
    {
        var s = Run(ESC + "[5;10H");
        Assert.Equal('H', s.Csis[0].Final);
        Assert.Equal(new[] { 5, 10 }, s.Csis[0].Parameters);
    }

    [Fact]
    public void Csi_with_no_params_reports_empty()
    {
        var s = Run(ESC + "[H");
        Assert.Equal('H', s.Csis[0].Final);
        Assert.Empty(s.Csis[0].Parameters);
    }

    [Fact]
    public void Csi_private_marker()
    {
        var s = Run(ESC + "[?25l" + ESC + "[?1049h");
        Assert.Equal('l', s.Csis[0].Final);
        Assert.Equal('?', s.Csis[0].Private);
        Assert.Equal(new[] { 25 }, s.Csis[0].Parameters);
        Assert.Equal('h', s.Csis[1].Final);
        Assert.Equal(new[] { 1049 }, s.Csis[1].Parameters);
    }

    [Fact]
    public void Csi_intermediate_byte()
    {
        var s = Run(ESC + "[!p"); // DECSTR soft reset
        Assert.Equal('p', s.Csis[0].Final);
        Assert.Equal('!', s.Csis[0].Intermediate);
    }

    [Fact]
    public void Osc_bel_and_st_terminated_with_utf8()
    {
        var s = Run(ESC + "]0;Title" + BEL + ESC + "]2;مرحبا" + ST);
        Assert.Equal("0;Title", s.Oscs[0]);
        Assert.Equal("2;مرحبا", s.Oscs[1]);
    }

    [Fact]
    public void Dcs_string_is_consumed_and_ignored()
    {
        var s = Run(ESC + "P1;2|junk" + ST + "after");
        Assert.Empty(s.Csis);
        Assert.Empty(s.Oscs);
        Assert.Equal("after", s.Printed.ToString()); // parser recovered to ground
    }

    [Fact]
    public void Esc_inside_csi_restarts_sequence()
    {
        var s = Run(ESC + "[31" + ESC + "[m");
        Assert.Single(s.Csis);
        Assert.Equal('m', s.Csis[0].Final);
    }

    [Fact]
    public void Can_aborts_csi_and_returns_to_ground()
    {
        var s = Run(ESC + "[31" + CAN + "m"); // CAN aborts the CSI
        Assert.Empty(s.Csis);
        Assert.Equal("m", s.Printed.ToString()); // 'm' printed as normal text
    }

    [Fact]
    public void Malformed_and_random_bytes_never_throw()
    {
        var sink = new RecordingSink();
        var parser = new VtParser(sink);
        var bytes = new byte[512];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((i * 73 + 17) & 0xFF); // deterministic pseudo-garbage incl. ESC/CSI bytes
        var ex = Record.Exception(() => parser.Feed(bytes));
        Assert.Null(ex);
    }

    [Fact]
    public void Recovers_to_printing_after_sequences()
    {
        var s = Run(ESC + "[1;32mOK" + ESC + "[0m done");
        Assert.Equal("OK done", s.Printed.ToString());
        Assert.Equal(2, s.Csis.Count);
    }
}
