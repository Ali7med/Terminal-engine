using System.Text;

namespace Terminal.Core.Vt;

/// <summary>
/// Receives the structural events produced by <see cref="VtParser"/>. This is the seam
/// between the parser (T-003) and the screen buffer (T-004): the parser owns the byte-level
/// state machine and UTF-8 decoding; the sink decides what each command means on the grid.
/// </summary>
public interface IVtParserSink
{
    /// <summary>A printable character (already UTF-8 decoded into a full Unicode scalar).</summary>
    void Print(Rune rune);

    /// <summary>A C0 control byte to execute in place (e.g. BEL, BS, HT, LF, VT, FF, CR).</summary>
    void Execute(byte control);

    /// <summary>
    /// An <c>ESC</c> sequence that is not CSI/OSC/DCS — e.g. <c>ESC 7</c> (DECSC),
    /// <c>ESC 8</c> (DECRC), <c>ESC M</c> (RI), <c>ESC c</c> (RIS), <c>ESC ( B</c> (charset).
    /// <paramref name="intermediate"/> is the collected intermediate byte or '\0'.
    /// </summary>
    void EscDispatch(char finalByte, char intermediate);

    /// <summary>
    /// A complete CSI sequence: <c>ESC [ &lt;private&gt; &lt;params&gt; &lt;intermediate&gt; &lt;final&gt;</c>.
    /// <paramref name="privateMarker"/> is one of '?','&gt;','&lt;','=' or '\0'.
    /// </summary>
    void CsiDispatch(char finalByte, VtParams parameters, char privateMarker, char intermediate);

    /// <summary>A complete OSC string (the payload between <c>ESC ]</c> and <c>BEL</c>/<c>ST</c>).</summary>
    void OscDispatch(string data);
}
