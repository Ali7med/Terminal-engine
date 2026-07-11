using System.Text;

namespace Terminal.Core.Vt;

/// <summary>
/// A byte-oriented VT/ANSI state machine (modelled on the vt100.net / Paul Williams
/// parser) with incremental UTF-8 decoding. It never touches a screen buffer — it turns
/// the raw PTY byte stream into structural events on <see cref="IVtParserSink"/>.
/// Unsupported or malformed sequences are dropped safely (never throws).
/// </summary>
public sealed class VtParser
{
    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        CsiIgnore,
        OscString,
        OscEscape,      // saw ESC inside an OSC string, waiting for the '\' of ST
        StringConsume,  // DCS/SOS/PM/APC body — consumed and discarded
        StringEscape,   // saw ESC inside a consumed string
    }

    private const int MaxParamLength = 256;
    private const int MaxOscLength = 8192;

    private readonly IVtParserSink _sink;
    private State _state = State.Ground;

    // CSI collection
    private readonly StringBuilder _params = new();
    private char _privateMarker;
    private char _intermediate;
    private bool _paramsOverflow;

    // OSC collection (raw bytes → decoded as UTF-8 at dispatch)
    private readonly List<byte> _oscBytes = new();
    private bool _oscOverflow;

    // Incremental UTF-8 assembly (Ground state only)
    private int _utf8Remaining;
    private int _utf8Codepoint;

    public VtParser(IVtParserSink sink) =>
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
            Feed(b);
    }

    public void Feed(byte b)
    {
        switch (_state)
        {
            case State.Ground: Ground(b); break;
            case State.Escape: Escape(b); break;
            case State.EscapeIntermediate: EscapeIntermediate(b); break;
            case State.CsiEntry: CsiEntry(b); break;
            case State.CsiParam: CsiParam(b); break;
            case State.CsiIntermediate: CsiIntermediate(b); break;
            case State.CsiIgnore: CsiIgnore(b); break;
            case State.OscString: OscStringByte(b); break;
            case State.OscEscape: OscEscapeByte(b); break;
            case State.StringConsume: StringConsumeByte(b); break;
            case State.StringEscape: StringEscapeByte(b); break;
        }
    }

    // ===== Ground (printable text + C0 controls + ESC) =====

    private void Ground(byte b)
    {
        // Middle of a multi-byte UTF-8 sequence?
        if (_utf8Remaining > 0)
        {
            if (b >= 0x80 && b <= 0xBF)
            {
                _utf8Codepoint = (_utf8Codepoint << 6) | (b & 0x3F);
                if (--_utf8Remaining == 0)
                    EmitCodepoint(_utf8Codepoint);
                return;
            }

            // Invalid continuation — emit replacement for the truncated sequence and reprocess b.
            _utf8Remaining = 0;
            _sink.Print(Rune.ReplacementChar);
            // fall through to handle b fresh
        }

        if (b == 0x1B) // ESC
        {
            _state = State.Escape;
            _intermediate = '\0';
            return;
        }

        if (b < 0x20) // C0 control
        {
            _sink.Execute(b);
            return;
        }

        if (b < 0x7F) // ASCII printable
        {
            _sink.Print(new Rune((char)b));
            return;
        }

        if (b == 0x7F) // DEL — ignored in ground per xterm
            return;

        // UTF-8 lead byte
        if (b >= 0xC2 && b <= 0xDF) { _utf8Remaining = 1; _utf8Codepoint = b & 0x1F; }
        else if (b >= 0xE0 && b <= 0xEF) { _utf8Remaining = 2; _utf8Codepoint = b & 0x0F; }
        else if (b >= 0xF0 && b <= 0xF4) { _utf8Remaining = 3; _utf8Codepoint = b & 0x07; }
        else _sink.Print(Rune.ReplacementChar); // 0x80..0xC1, 0xF5..0xFF are invalid leads
    }

    private void EmitCodepoint(int codepoint) =>
        _sink.Print(Rune.TryCreate(codepoint, out var rune) ? rune : Rune.ReplacementChar);

    // ===== ESC =====

    private void Escape(byte b)
    {
        if (b == 0x1B) { _state = State.Escape; _intermediate = '\0'; return; }
        if (b == 0x18 || b == 0x1A) { _state = State.Ground; return; } // CAN / SUB abort
        if (b < 0x20) { _sink.Execute(b); return; }                    // C0 executed in place

        switch (b)
        {
            case (byte)'[': BeginCsi(); return;
            case (byte)']': BeginOsc(); return;
            case (byte)'P': // DCS
            case (byte)'X': // SOS
            case (byte)'^': // PM
            case (byte)'_': // APC
                _state = State.StringConsume;
                return;
        }

        if (b >= 0x20 && b <= 0x2F) // intermediate (e.g. ' ', '#', '(')
        {
            _intermediate = (char)b;
            _state = State.EscapeIntermediate;
            return;
        }

        if (b >= 0x30 && b <= 0x7E) // final
        {
            _sink.EscDispatch((char)b, _intermediate);
            _state = State.Ground;
            return;
        }

        _state = State.Ground; // anything else: abort
    }

    private void EscapeIntermediate(byte b)
    {
        if (b == 0x1B) { _state = State.Escape; _intermediate = '\0'; return; }
        if (b == 0x18 || b == 0x1A) { _state = State.Ground; return; }
        if (b < 0x20) { _sink.Execute(b); return; }

        if (b >= 0x20 && b <= 0x2F) { _intermediate = (char)b; return; } // collect (keep last)

        if (b >= 0x30 && b <= 0x7E)
        {
            _sink.EscDispatch((char)b, _intermediate);
            _state = State.Ground;
            return;
        }

        _state = State.Ground;
    }

    // ===== CSI =====

    private void BeginCsi()
    {
        _params.Clear();
        _privateMarker = '\0';
        _intermediate = '\0';
        _paramsOverflow = false;
        _state = State.CsiEntry;
    }

    private void CsiEntry(byte b)
    {
        if (CsiCommon(b)) return;

        if (b >= 0x3C && b <= 0x3F) { _privateMarker = (char)b; _state = State.CsiParam; return; } // <=>?
        if ((b >= 0x30 && b <= 0x39) || b == 0x3B || b == 0x3A) { AppendParam(b); _state = State.CsiParam; return; }
        if (b >= 0x20 && b <= 0x2F) { _intermediate = (char)b; _state = State.CsiIntermediate; return; }
        if (b >= 0x40 && b <= 0x7E) { DispatchCsi((char)b); return; }

        _state = State.CsiIgnore;
    }

    private void CsiParam(byte b)
    {
        if (CsiCommon(b)) return;

        if ((b >= 0x30 && b <= 0x39) || b == 0x3B || b == 0x3A) { AppendParam(b); return; }
        if (b >= 0x3C && b <= 0x3F) { _state = State.CsiIgnore; return; } // private marker out of place
        if (b >= 0x20 && b <= 0x2F) { _intermediate = (char)b; _state = State.CsiIntermediate; return; }
        if (b >= 0x40 && b <= 0x7E) { DispatchCsi((char)b); return; }

        _state = State.CsiIgnore;
    }

    private void CsiIntermediate(byte b)
    {
        if (CsiCommon(b)) return;

        if (b >= 0x20 && b <= 0x2F) { _intermediate = (char)b; return; }
        if (b >= 0x40 && b <= 0x7E) { DispatchCsi((char)b); return; }
        if (b >= 0x30 && b <= 0x3F) { _state = State.CsiIgnore; return; } // param after intermediate

        _state = State.CsiIgnore;
    }

    private void CsiIgnore(byte b)
    {
        if (b == 0x1B) { _state = State.Escape; _intermediate = '\0'; return; }
        if (b == 0x18 || b == 0x1A) { _state = State.Ground; return; }
        if (b >= 0x40 && b <= 0x7E) _state = State.Ground; // final consumes the sequence
    }

    /// <summary>Handles bytes common to all CSI sub-states (ESC restart, CAN/SUB, C0). Returns true if consumed.</summary>
    private bool CsiCommon(byte b)
    {
        if (b == 0x1B) { _state = State.Escape; _intermediate = '\0'; return true; }
        if (b == 0x18 || b == 0x1A) { _state = State.Ground; return true; }
        if (b < 0x20) { _sink.Execute(b); return true; }
        return false;
    }

    private void AppendParam(byte b)
    {
        if (_params.Length < MaxParamLength)
            _params.Append((char)b);
        else
            _paramsOverflow = true;
    }

    private void DispatchCsi(char finalByte)
    {
        if (!_paramsOverflow)
        {
            var parameters = VtParams.Parse(_params.ToString());
            _sink.CsiDispatch(finalByte, parameters, _privateMarker, _intermediate);
        }
        _state = State.Ground;
    }

    // ===== OSC =====

    private void BeginOsc()
    {
        _oscBytes.Clear();
        _oscOverflow = false;
        _state = State.OscString;
    }

    private void OscStringByte(byte b)
    {
        if (b == 0x07) { DispatchOsc(); return; }          // BEL terminates
        if (b == 0x1B) { _state = State.OscEscape; return; } // maybe ST (ESC \)
        if (b == 0x18 || b == 0x1A) { _state = State.Ground; return; } // CAN/SUB abort

        if (_oscBytes.Count < MaxOscLength)
            _oscBytes.Add(b);
        else
            _oscOverflow = true;
    }

    private void OscEscapeByte(byte b)
    {
        DispatchOsc(); // ST (ESC \) or an aborting ESC — either way, close the OSC
        if (b != (byte)'\\')
        {
            // The ESC started a new sequence; reprocess this byte from Ground.
            Feed(b);
        }
    }

    private void DispatchOsc()
    {
        if (!_oscOverflow)
        {
            string data = _oscBytes.Count == 0
                ? string.Empty
                : Encoding.UTF8.GetString(_oscBytes.ToArray());
            _sink.OscDispatch(data);
        }
        _oscBytes.Clear();
        _state = State.Ground;
    }

    // ===== DCS / SOS / PM / APC — consumed and ignored =====

    private void StringConsumeByte(byte b)
    {
        if (b == 0x1B) { _state = State.StringEscape; return; }
        if (b == 0x07 || b == 0x18 || b == 0x1A) _state = State.Ground; // BEL/CAN/SUB end it
    }

    private void StringEscapeByte(byte b)
    {
        _state = State.Ground;
        if (b != (byte)'\\')
            Feed(b); // reprocess the ESC-initiated byte
    }
}
