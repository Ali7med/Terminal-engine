using System.Collections.Generic;
using System.Text;
using Terminal.Core.Vt;

namespace Terminal.Core.Screen;

/// <summary>
/// The in-memory screen (guide T-004). Implements <see cref="IVtParserSink"/>: the parser
/// (T-003) turns bytes into structural events and this class gives them meaning on a 2D grid —
/// cursor movement (CUP/HVP/CUU/CUD/CUF/CUB/CHA/VPA/CNL/CPL), save/restore (DECSC/DECRC, SCO),
/// erase (ED/EL/ECH), scroll region (DECSTBM) with insert/delete/scroll (IL/DL/ICH/DCH/SU/SD),
/// SGR styling, and the alternate screen (?1049/?47/?1047) for full-screen apps. The main
/// screen keeps a bounded scrollback ring; the alternate screen keeps none.
/// </summary>
public sealed class ScreenBuffer : IVtParserSink
{
    public const int DefaultScrollback = 10_000;

    private const int MinCols = 1;
    private const int MinRows = 1;
    private const int TabWidth = 8;

    private static readonly Rune Space = new(' ');

    private readonly VtParser _parser;
    private readonly StyleTable _styles = new();
    private readonly LinkTable _links = new();
    private readonly ScrollbackBuffer _scrollback;

    private int _cols;
    private int _rows;

    private Cell[][] _grid;
    private Cell[][]? _altGrid;
    private bool _altActive;

    private int _cursorRow;
    private int _cursorCol;
    private bool _wrapPending;   // deferred wrap: last glyph landed on the final column

    private int _savedRow;
    private int _savedCol;
    private TerminalStyle _savedStyle = TerminalStyle.Default;

    private TerminalStyle _style = TerminalStyle.Default;
    private int _styleId;        // interned id of _style (0 == default)
    private int _linkId;         // interned id of the active OSC 8 hyperlink (0 == none)

    private int _scrollTop;      // inclusive, 0-based
    private int _scrollBottom;   // inclusive, 0-based

    private bool _autoWrap = true;
    private bool _cursorVisible = true;
    private bool _applicationCursorKeys;
    private bool _bracketedPaste;

    // ===== mouse reporting (DEC ?1000/?1002/?1003 + ?1006 SGR) =====
    private enum MouseTracking { Off, Click, ButtonEvent, AnyEvent }
    private MouseTracking _mouseMode = MouseTracking.Off;
    private bool _mouseSgr;

    private long _dirtyFrom;     // smallest absolute line changed since the last snapshot

    // ===== command blocks (OSC 133 shell integration + heuristic) =====
    private const int MaxBlocks = 400;
    private readonly List<Block> _blocks = new();
    private Block? _openBlock;

    public ScreenBuffer(int cols = 80, int rows = 24, int scrollbackCapacity = DefaultScrollback)
    {
        _cols = Math.Max(MinCols, cols);
        _rows = Math.Max(MinRows, rows);
        _scrollback = new ScrollbackBuffer(Math.Max(0, scrollbackCapacity));
        _grid = NewGrid(_rows, _cols);
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
        _parser = new VtParser(this);
        _dirtyFrom = 0; // a fresh buffer needs a full first paint
    }

    // ===== public state =====

    public int Cols => _cols;
    public int Rows => _rows;
    public int CursorRow => _cursorRow;
    public int CursorCol => _cursorCol;
    public bool CursorVisible => _cursorVisible;
    public bool IsAltScreen => _altActive;
    public bool AutoWrap => _autoWrap;
    public bool ApplicationCursorKeys => _applicationCursorKeys;
    public bool BracketedPaste => _bracketedPaste;

    /// <summary>
    /// Raised when the application queries the terminal (DSR/DA/DECRQM) and expects a reply
    /// written back to the PTY. The host must subscribe and forward the string to the session's
    /// input; without replies, TUI apps (e.g. Claude Code/Ink) lose cursor sync — stray characters
    /// that never erase — and degrade their UI after probe timeouts.
    /// </summary>
    public event System.Action<string>? ResponseRequested;

    private void Respond(string reply) => ResponseRequested?.Invoke(reply);

    /// <summary>Does the application want mouse events reported?</summary>
    public bool MouseReportingEnabled => _mouseMode != MouseTracking.Off;

    /// <summary>Does the application want drag events (motion with a button down)?</summary>
    public bool MouseReportsDrag => _mouseMode is MouseTracking.ButtonEvent or MouseTracking.AnyEvent;

    /// <summary>Does the application want all motion (even with no button down)?</summary>
    public bool MouseReportsAllMotion => _mouseMode == MouseTracking.AnyEvent;

    /// <summary>
    /// Encodes a mouse event as a VT sequence for the active mode (SGR 1006 or X10 1000);
    /// null when reporting is off. <paramref name="col"/>/<paramref name="row"/> are 0-based cells.
    /// </summary>
    public string? EncodeMouse(MouseButton button, MouseEventType type, int col, int row,
        bool shift = false, bool alt = false, bool ctrl = false)
    {
        if (_mouseMode == MouseTracking.Off) return null;
        col = Math.Max(0, col);
        row = Math.Max(0, row);
        int mods = (shift ? 4 : 0) + (alt ? 8 : 0) + (ctrl ? 16 : 0);
        int motion = type == MouseEventType.Move ? 32 : 0;

        if (_mouseSgr)
        {
            int b = (int)button + mods + motion;
            char final = type == MouseEventType.Release ? 'm' : 'M';
            return $"[<{b};{col + 1};{row + 1}{final}";
        }

        // X10/normal: (Cb,Cx,Cy) offset by 32; release = button 3; coords capped at 223.
        int baseBtn = type == MouseEventType.Release ? 3 : (int)button;
        int cb = baseBtn + mods + motion + 32;
        int cx = Math.Min(col + 1, 223) + 32;
        int cy = Math.Min(row + 1, 223) + 32;
        return $"[M{(char)cb}{(char)cx}{(char)cy}";
    }
    public int ScrollbackCount => _scrollback.Count;
    public string Title { get; private set; } = string.Empty;
    public StyleTable Styles => _styles;
    public LinkTable Links => _links;

    /// <summary>Absolute line number of the top visible grid row.</summary>
    public long GridTopLine => _scrollback.Evicted + _scrollback.Count;

    /// <summary>Absolute line number of the cursor's row (main-screen scale).</summary>
    private long CursorAbsLine => GridTopLine + _cursorRow;

    /// <summary>Smallest absolute line changed since the last <see cref="Snapshot"/>.</summary>
    public long DirtyFromLine => _dirtyFrom;

    // ===== feeding bytes (integration point with IPtySession.DataReceived) =====

    public void Feed(ReadOnlySpan<byte> bytes) => _parser.Feed(bytes);

    public void Feed(byte b) => _parser.Feed(b);

    public void FeedString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _parser.Feed(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Resets the entire terminal state (as RIS / ESC c does).</summary>
    public void Clear() => Reset();

    // ===== IVtParserSink =====

    public void Print(Rune rune)
    {
        int width = UnicodeWidth.Measure(rune);
        if (width <= 0)
            return; // zero-width (combining marks / ZWJ) are not stored as their own cell (known limitation)

        if (_wrapPending)
        {
            _cursorCol = 0;
            LineFeed();
            _wrapPending = false;
        }

        // A double-width glyph cannot occupy the single final column.
        if (width == 2 && _cursorCol == _cols - 1)
        {
            if (_autoWrap)
            {
                Active[_cursorRow][_cursorCol] = new Cell(Space, _styleId);
                MarkRowDirty(_cursorRow);
                _cursorCol = 0;
                LineFeed();
            }
            else
            {
                _cursorCol = _cols - 2;
            }
        }

        var row = Active[_cursorRow];
        BreakWideNeighbour(row, _cursorCol);
        row[_cursorCol] = new Cell(rune, _styleId, _linkId);
        if (width == 2 && _cursorCol + 1 < _cols)
        {
            BreakWideNeighbour(row, _cursorCol + 1);
            row[_cursorCol + 1] = Cell.WideTrailing(_styleId, _linkId);
        }
        MarkRowDirty(_cursorRow);

        _cursorCol += width;
        if (_cursorCol >= _cols)
        {
            _cursorCol = _cols - 1;
            _wrapPending = _autoWrap;
        }
    }

    public void Execute(byte control)
    {
        switch (control)
        {
            case 0x08: Backspace(); break;                   // BS
            case 0x09: Tab(); break;                         // HT
            case 0x0A:                                        // LF
            case 0x0B:                                        // VT
            case 0x0C: LineFeed(); break;                    // FF
            case 0x0D: CarriageReturn(); break;              // CR
            default: break;                                   // BEL and others: no grid effect
        }
    }

    public void EscDispatch(char finalByte, char intermediate)
    {
        if (intermediate != '\0')
            return; // charset designation etc. — no grid effect for the MVP

        switch (finalByte)
        {
            case '7': SaveCursor(); break;                    // DECSC
            case '8': RestoreCursor(); break;                 // DECRC
            case 'D': LineFeed(); break;                      // IND
            case 'M': ReverseIndex(); break;                  // RI
            case 'E': CarriageReturn(); LineFeed(); break;    // NEL
            case 'c': Reset(); break;                          // RIS
            default: break;                                    // DECKPAM/DECKPNM etc. ignored
        }
    }

    public void CsiDispatch(char finalByte, VtParams p, char privateMarker, char intermediate)
    {
        if (intermediate != '\0')
            return; // DECSTR (!p), DECSCUSR (SP q) etc. — not needed for the grid MVP

        switch (finalByte)
        {
            case 'm': SetStyle(SgrProcessor.Apply(_style, p)); break;

            case 'H':
            case 'f': MoveTo(Amount(p, 0) - 1, Amount(p, 1) - 1); break;     // CUP / HVP
            case 'A': MoveTo(_cursorRow - Amount(p, 0), _cursorCol); break;  // CUU
            case 'B': MoveTo(_cursorRow + Amount(p, 0), _cursorCol); break;  // CUD
            case 'C': MoveTo(_cursorRow, _cursorCol + Amount(p, 0)); break;  // CUF
            case 'D': MoveTo(_cursorRow, _cursorCol - Amount(p, 0)); break;  // CUB
            case 'E': MoveTo(_cursorRow + Amount(p, 0), 0); break;           // CNL
            case 'F': MoveTo(_cursorRow - Amount(p, 0), 0); break;           // CPL
            case 'G':
            case '`': MoveTo(_cursorRow, Amount(p, 0) - 1); break;           // CHA / HPA
            case 'd': MoveTo(Amount(p, 0) - 1, _cursorCol); break;           // VPA

            case 's': if (privateMarker == '\0') SaveCursor(); break;        // SCO save
            case 'u': if (privateMarker == '\0') RestoreCursor(); break;     // SCO restore

            case 'J': EraseInDisplay(p.Get(0, 0)); break;                    // ED
            case 'K': EraseInLine(p.Get(0, 0)); break;                       // EL
            case 'X': EraseChars(Amount(p, 0)); break;                       // ECH

            case 'L': InsertLines(Amount(p, 0)); break;                      // IL
            case 'M': DeleteLines(Amount(p, 0)); break;                      // DL
            case 'P': DeleteChars(Amount(p, 0)); break;                      // DCH
            case '@': InsertChars(Amount(p, 0)); break;                      // ICH

            case 'S': ScrollUp(Amount(p, 0)); break;                         // SU
            case 'T': ScrollDown(Amount(p, 0)); break;                       // SD

            case 'r': if (privateMarker == '\0') SetScrollRegion(p); break;  // DECSTBM

            case 'h': if (privateMarker == '?') SetDecModes(p, true); break;
            case 'l': if (privateMarker == '?') SetDecModes(p, false); break;

            // ===== terminal queries — the app expects a reply on stdin =====

            // DSR: CSI 5n status → OK; CSI 6n cursor position → CPR (1-based). DECXCPR (?6n) likewise.
            case 'n' when privateMarker is '\0' or '?':
                switch (p.Get(0, 0))
                {
                    case 5: Respond("\x1b[0n"); break;
                    case 6:
                        Respond(privateMarker == '?'
                            ? $"\x1b[?{_cursorRow + 1};{_cursorCol + 1}R"
                            : $"\x1b[{_cursorRow + 1};{_cursorCol + 1}R");
                        break;
                }
                break;

            // DA1: identify as VT220 with ANSI colour. Apps commonly send probes then DA1; the DA1
            // reply doubles as the "end of probing" marker, so answering it avoids probe timeouts.
            case 'c' when privateMarker == '\0' && intermediate == '\0':
                Respond("\x1b[?62;22c");
                break;

            // DA2 (secondary): terminal type/version. Conservative VT100-ish reply (like ConPTY).
            case 'c' when privateMarker == '>':
                Respond("\x1b[>0;10;1c");
                break;

            // DECRQM (CSI ? Pm $ p): report private-mode state — 1 set · 2 reset · 0 unrecognized.
            // Answering 0 for modes we don't implement (e.g. 2026 synchronized output) makes apps
            // skip them cleanly instead of waiting on a timeout.
            case 'p' when privateMarker == '?' && intermediate == '$':
            {
                int mode = p.Get(0, 0);
                int state = mode switch
                {
                    25 => _cursorVisible ? 1 : 2,
                    7 => _autoWrap ? 1 : 2,
                    2004 => _bracketedPaste ? 1 : 2,
                    47 or 1047 or 1049 => _altActive ? 1 : 2,
                    1000 => _mouseMode == MouseTracking.Click ? 1 : 2,
                    1002 => _mouseMode == MouseTracking.ButtonEvent ? 1 : 2,
                    1003 => _mouseMode == MouseTracking.AnyEvent ? 1 : 2,
                    1006 => _mouseSgr ? 1 : 2,
                    _ => 0,
                };
                Respond($"\x1b[?{mode};{state}$y");
                break;
            }

            default: break; // anything else doesn't affect the grid and expects no reply
        }
    }

    public void OscDispatch(string data)
    {
        if (string.IsNullOrEmpty(data))
            return;

        int semi = data.IndexOf(';');
        string ps = semi < 0 ? data : data[..semi];

        // OSC 0 = icon+window title, OSC 2 = window title.
        if (ps is "0" or "2")
            Title = semi < 0 ? string.Empty : data[(semi + 1)..];

        // OSC 133 = shell integration (command blocks).
        else if (ps == "133")
            ShellIntegration(semi < 0 ? string.Empty : data[(semi + 1)..]);

        // OSC 8 = hyperlink (attaches a URI to subsequently printed cells).
        else if (ps == "8")
            SetHyperlink(semi < 0 ? string.Empty : data[(semi + 1)..]);

        // OSC 10/11 (default colours) also arrive here; they have no consumer yet.
    }

    /// <summary>
    /// OSC 8: <c>params;URI</c>. An empty URI ends the current link. Full form is
    /// <c>ESC ] 8 ; params ; URI ST … ESC ] 8 ; ; ST</c>; the rarely-used params field is skipped.
    /// </summary>
    private void SetHyperlink(string rest)
    {
        int semi = rest.IndexOf(';');
        string uri = semi < 0 ? string.Empty : rest[(semi + 1)..];
        _linkId = _links.Intern(uri);   // Intern maps null/empty → 0 (no link)
    }

    // ===== OSC 133 shell-integration command blocks =====

    /// <summary>OSC 133: A = prompt start, B = command-input start, C = output start, D[;code] = end.</summary>
    private void ShellIntegration(string rest)
    {
        char kind = rest.Length > 0 ? rest[0] : '\0';
        switch (kind)
        {
            case 'A': BeginPrompt(); break;
            case 'C': BeginOutput(); break;
            case 'D': CloseOpenBlock(ParseExitCode(rest)); break;
            // 'B' = end of prompt / start of command input — no grid effect.
        }
    }

    private static int? ParseExitCode(string rest)
    {
        int semi = rest.IndexOf(';');
        if (semi < 0) return null;
        string code = rest[(semi + 1)..];
        int end = code.IndexOf(';');
        if (end >= 0) code = code[..end];
        return int.TryParse(code, out int v) ? v : null;
    }

    private void BeginPrompt()
    {
        CloseOpenBlock(null);
        long start = CursorAbsLine;
        _openBlock = new Block { StartLine = start, OutputStartLine = start };
        _blocks.Add(_openBlock);
        TrimBlocks();
    }

    private void BeginOutput()
    {
        if (_openBlock != null) _openBlock.OutputStartLine = CursorAbsLine;
    }

    private void CloseOpenBlock(int? exitCode)
    {
        if (_openBlock == null) return;
        _openBlock.EndLine = CursorAbsLine;
        _openBlock.ExitCode = exitCode;
        _openBlock.State = exitCode is null || exitCode == 0 ? BlockState.Success : BlockState.Failed;
        _openBlock = null;
    }

    /// <summary>
    /// Heuristic block open (no shell integration): called by the UI when a command is sent
    /// (saved command / Enter). Ignored on the alternate screen or while an OSC 133 block is open.
    /// </summary>
    public void BeginHeuristicCommand(string commandText)
    {
        if (_altActive) return;
        if (_openBlock != null && _openBlock.EndLine == long.MaxValue) return;
        long start = CursorAbsLine;
        _openBlock = new Block
        {
            StartLine = start,
            OutputStartLine = start + 1,
            CommandText = commandText?.Trim() ?? string.Empty,
        };
        _blocks.Add(_openBlock);
        TrimBlocks();
    }

    private void TrimBlocks()
    {
        if (_blocks.Count > MaxBlocks)
            _blocks.RemoveRange(0, _blocks.Count - MaxBlocks);
        long baseLine = _scrollback.Evicted;
        int drop = 0;
        while (drop < _blocks.Count
               && _blocks[drop].EndLine != long.MaxValue
               && _blocks[drop].EndLine <= baseLine)
            drop++;
        if (drop > 0) _blocks.RemoveRange(0, drop);
    }

    private IReadOnlyList<BlockSnapshot> FreezeBlocks()
    {
        if (_blocks.Count == 0) return Array.Empty<BlockSnapshot>();
        var list = new List<BlockSnapshot>(_blocks.Count);
        foreach (var b in _blocks) list.Add(new BlockSnapshot(b));
        return list;
    }

    // ===== cursor & line movement =====

    private void MoveTo(int row, int col)
    {
        _cursorRow = Math.Clamp(row, 0, _rows - 1);
        _cursorCol = Math.Clamp(col, 0, _cols - 1);
        _wrapPending = false;
    }

    private void CarriageReturn()
    {
        _cursorCol = 0;
        _wrapPending = false;
    }

    private void Backspace()
    {
        if (_cursorCol > 0)
            _cursorCol--;
        _wrapPending = false;
    }

    private void Tab()
    {
        int next = ((_cursorCol / TabWidth) + 1) * TabWidth;
        _cursorCol = Math.Min(next, _cols - 1);
        _wrapPending = false;
    }

    /// <summary>Line feed (IND): moves down one row, scrolling the region when at its bottom.</summary>
    private void LineFeed()
    {
        _wrapPending = false;
        if (_cursorRow == _scrollBottom)
            ScrollUp(1);
        else if (_cursorRow < _rows - 1)
            _cursorRow++;
    }

    /// <summary>Reverse index (RI): moves up one row, scrolling the region down when at its top.</summary>
    private void ReverseIndex()
    {
        _wrapPending = false;
        if (_cursorRow == _scrollTop)
            ScrollDown(1);
        else if (_cursorRow > 0)
            _cursorRow--;
    }

    private void SaveCursor()
    {
        _savedRow = _cursorRow;
        _savedCol = _cursorCol;
        _savedStyle = _style;
    }

    private void RestoreCursor()
    {
        _cursorRow = Math.Clamp(_savedRow, 0, _rows - 1);
        _cursorCol = Math.Clamp(_savedCol, 0, _cols - 1);
        SetStyle(_savedStyle);
        _wrapPending = false;
    }

    private void SetScrollRegion(VtParams p)
    {
        int top = Amount(p, 0) - 1;
        int bottom = p.Get(1, _rows);
        if (bottom <= 0)
            bottom = _rows;

        int topIdx = Math.Clamp(top, 0, _rows - 1);
        int bottomIdx = Math.Clamp(bottom - 1, 0, _rows - 1);
        if (bottomIdx <= topIdx)
        {
            _scrollTop = 0;
            _scrollBottom = _rows - 1;
        }
        else
        {
            _scrollTop = topIdx;
            _scrollBottom = bottomIdx;
        }
        MoveTo(0, 0);
    }

    // ===== erase =====

    private void EraseInLine(int mode)
    {
        var row = Active[_cursorRow];
        var blank = new Cell(Space, _styleId);
        switch (mode)
        {
            case 0: for (int c = _cursorCol; c < _cols; c++) row[c] = blank; break;
            case 1: for (int c = 0; c <= _cursorCol && c < _cols; c++) row[c] = blank; break;
            case 2: for (int c = 0; c < _cols; c++) row[c] = blank; break;
            default: return;
        }
        // xterm يُلغي الالتفاف المؤجَّل عند المسح: بدونه، مؤشّرٌ واقفٌ في آخر عمود مع التفافٍ معلّق
        // يقفز للسطر التالي عند أوّل حرف بعد `CSI K` بدل أن يكتب فوق آخر عمود ⇒ شبحٌ عند الحافّة.
        _wrapPending = false;
        MarkRowDirty(_cursorRow);
    }

    private void EraseInDisplay(int mode)
    {
        var grid = Active;
        var blank = new Cell(Space, _styleId);
        switch (mode)
        {
            case 0:
                EraseInLine(0);
                for (int r = _cursorRow + 1; r < _rows; r++) FillRow(grid[r], blank);
                break;
            case 1:
                for (int r = 0; r < _cursorRow; r++) FillRow(grid[r], blank);
                EraseInLine(1);
                break;
            case 2:
            case 3:
                for (int r = 0; r < _rows; r++) FillRow(grid[r], blank);
                if (mode == 3 && !_altActive)
                    _scrollback.Clear();
                break;
            default: return;
        }
        _wrapPending = false;   // كما في EraseInLine
        MarkAllDirty();
    }

    private void EraseChars(int n)
    {
        var row = Active[_cursorRow];
        var blank = new Cell(Space, _styleId);
        int end = Math.Min(_cursorCol + n, _cols);
        for (int c = _cursorCol; c < end; c++) row[c] = blank;
        _wrapPending = false;   // كما في EraseInLine
        MarkRowDirty(_cursorRow);
    }

    // ===== insert / delete =====

    private void InsertLines(int n)
    {
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom)
            return;
        var grid = Active;
        n = Math.Min(n, _scrollBottom - _cursorRow + 1);
        for (int k = 0; k < n; k++)
        {
            for (int r = _scrollBottom; r > _cursorRow; r--)
                grid[r] = grid[r - 1];
            grid[_cursorRow] = BlankRow();
        }
        _cursorCol = 0;
        _wrapPending = false;
        MarkAllDirty();
    }

    private void DeleteLines(int n)
    {
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom)
            return;
        var grid = Active;
        n = Math.Min(n, _scrollBottom - _cursorRow + 1);
        for (int k = 0; k < n; k++)
        {
            for (int r = _cursorRow; r < _scrollBottom; r++)
                grid[r] = grid[r + 1];
            grid[_scrollBottom] = BlankRow();
        }
        _cursorCol = 0;
        _wrapPending = false;
        MarkAllDirty();
    }

    private void InsertChars(int n)
    {
        var row = Active[_cursorRow];
        n = Math.Min(n, _cols - _cursorCol);
        var blank = new Cell(Space, _styleId);
        for (int c = _cols - 1; c >= _cursorCol + n; c--) row[c] = row[c - n];
        for (int c = _cursorCol; c < _cursorCol + n; c++) row[c] = blank;
        MarkRowDirty(_cursorRow);
    }

    private void DeleteChars(int n)
    {
        var row = Active[_cursorRow];
        n = Math.Min(n, _cols - _cursorCol);
        var blank = new Cell(Space, _styleId);
        for (int c = _cursorCol; c < _cols - n; c++) row[c] = row[c + n];
        for (int c = _cols - n; c < _cols; c++) row[c] = blank;
        MarkRowDirty(_cursorRow);
    }

    // ===== scrolling =====

    /// <summary>Scrolls the scroll region up <paramref name="n"/> rows; on the main full screen the
    /// rows that leave the top enter scrollback.</summary>
    private void ScrollUp(int n)
    {
        if (n <= 0)
            return;
        n = Math.Min(n, _scrollBottom - _scrollTop + 1);
        var grid = Active;
        bool toScrollback = !_altActive && _scrollTop == 0 && _scrollBottom == _rows - 1;

        for (int k = 0; k < n; k++)
        {
            if (toScrollback)
                _scrollback.Add(grid[_scrollTop]);
            for (int r = _scrollTop; r < _scrollBottom; r++)
                grid[r] = grid[r + 1];
            grid[_scrollBottom] = BlankRow();
        }
        MarkAllDirty();
    }

    /// <summary>Scrolls the scroll region down <paramref name="n"/> rows (opening blank rows at the top).</summary>
    private void ScrollDown(int n)
    {
        if (n <= 0)
            return;
        n = Math.Min(n, _scrollBottom - _scrollTop + 1);
        var grid = Active;
        for (int k = 0; k < n; k++)
        {
            for (int r = _scrollBottom; r > _scrollTop; r--)
                grid[r] = grid[r - 1];
            grid[_scrollTop] = BlankRow();
        }
        MarkAllDirty();
    }

    // ===== DEC private modes =====

    private void SetDecModes(VtParams p, bool set)
    {
        for (int i = 0; i < p.Count; i++)
        {
            switch (p.Get(i, 0))
            {
                case 1: _applicationCursorKeys = set; break;   // DECCKM (consumed by T-006 input)
                case 7: _autoWrap = set; break;                // DECAWM
                case 25: _cursorVisible = set; break;          // DECTCEM
                case 47:
                case 1047:
                case 1049:
                    if (set) EnterAltScreen(save: p.Get(i, 0) == 1049);
                    else LeaveAltScreen(restore: p.Get(i, 0) == 1049);
                    break;
                case 1000: _mouseMode = set ? MouseTracking.Click : MouseTracking.Off; break;
                case 1002: _mouseMode = set ? MouseTracking.ButtonEvent : MouseTracking.Off; break;
                case 1003: _mouseMode = set ? MouseTracking.AnyEvent : MouseTracking.Off; break;
                case 1006: _mouseSgr = set; break;
                case 2004: _bracketedPaste = set; break;       // bracketed paste (consumed by T-006 input)
                default: break;
            }
        }
    }

    private void EnterAltScreen(bool save)
    {
        if (_altActive)
            return;
        if (save)
            SaveCursor();
        _altGrid = NewGrid(_rows, _cols);
        _altActive = true;
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
        _cursorRow = 0;
        _cursorCol = 0;
        _wrapPending = false;
        MarkAllDirty();
    }

    private void LeaveAltScreen(bool restore)
    {
        if (!_altActive)
            return;
        _altActive = false;
        _altGrid = null;
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
        if (restore)
            RestoreCursor();
        _wrapPending = false;
        MarkAllDirty();
    }

    // ===== resize =====

    /// <summary>Re-lays the grid to new dimensions (guide T-004.2). Scrollback is preserved; on a
    /// vertical shrink the overflowing top rows of the main screen are pushed into scrollback.</summary>
    public void Resize(int cols, int rows)
    {
        cols = Math.Max(MinCols, cols);
        rows = Math.Max(MinRows, rows);
        if (cols == _cols && rows == _rows)
            return;

        _grid = ResizeGrid(_grid, rows, cols, pushOverflow: !_altActive);
        if (_altGrid != null)
            _altGrid = ResizeGrid(_altGrid, rows, cols, pushOverflow: false);

        _cols = cols;
        _rows = rows;
        _cursorRow = Math.Clamp(_cursorRow, 0, rows - 1);
        _cursorCol = Math.Clamp(_cursorCol, 0, cols - 1);
        _savedRow = Math.Clamp(_savedRow, 0, rows - 1);
        _savedCol = Math.Clamp(_savedCol, 0, cols - 1);
        _scrollTop = 0;
        _scrollBottom = rows - 1;
        _wrapPending = false;
        _dirtyFrom = 0; // reflow ⇒ full repaint
    }

    private Cell[][] ResizeGrid(Cell[][] old, int newRows, int newCols, bool pushOverflow)
    {
        int oldRows = old.Length;
        var ng = NewGrid(newRows, newCols);

        int overflow = Math.Max(0, oldRows - newRows);
        if (overflow > 0 && pushOverflow)
        {
            for (int r = 0; r < overflow; r++)
                _scrollback.Add(old[r]);
        }

        int srcStart = overflow;
        int copyRows = Math.Min(newRows, oldRows - overflow);
        for (int r = 0; r < copyRows; r++)
        {
            var src = old[srcStart + r];
            var dst = ng[r];
            int copyCols = Math.Min(newCols, src.Length);
            for (int c = 0; c < copyCols; c++) dst[c] = src[c];
        }
        return ng;
    }

    // ===== snapshot =====

    /// <summary>Immutable frame for the render thread; resets dirty tracking (guide T-004.5).</summary>
    public ScreenSnapshot Snapshot()
    {
        var grid = Active;
        int scrollbackLines = _altActive ? 0 : _scrollback.Count;
        var lines = new List<StyledRun[]>(scrollbackLines + _rows);

        for (int i = 0; i < scrollbackLines; i++)
            lines.Add(FreezeRow(_scrollback[i]));
        for (int r = 0; r < _rows; r++)
            lines.Add(FreezeRow(grid[r]));

        long baseLine = _altActive ? GridTopLine : _scrollback.Evicted;
        TrimBlocks();
        var snap = new ScreenSnapshot(
            _rows, _cols, _cursorRow, _cursorCol, _cursorVisible,
            _altActive, baseLine, _dirtyFrom, lines, FreezeBlocks());
        _dirtyFrom = long.MaxValue;
        return snap;
    }

    private StyledRun[] FreezeRow(Cell[] cells)
    {
        int end = cells.Length;
        while (end > 0)
        {
            var cell = cells[end - 1];
            if (!cell.IsWideTrailing && cell.Codepoint == ' ' && cell.StyleId == 0)
                end--;
            else
                break;
        }
        if (end == 0)
            return Array.Empty<StyledRun>();

        var runs = new List<StyledRun>();
        var sb = new StringBuilder();
        int currentStyleId = -1;
        int currentLinkId = 0;
        TerminalStyle currentStyle = TerminalStyle.Default;
        string? currentLink = null;

        for (int i = 0; i < end; i++)
        {
            var cell = cells[i];
            if (cell.IsWideTrailing)
                continue; // the lead cell's glyph already spans this column

            if (cell.StyleId != currentStyleId || cell.LinkId != currentLinkId)
            {
                if (sb.Length > 0)
                {
                    runs.Add(new StyledRun(sb.ToString(), currentStyle, currentLink));
                    sb.Clear();
                }
                currentStyleId = cell.StyleId;
                currentStyle = _styles.Resolve(currentStyleId);
                currentLinkId = cell.LinkId;
                currentLink = _links.Resolve(currentLinkId);
            }
            sb.Append(cell.Rune.ToString());
        }
        if (sb.Length > 0)
            runs.Add(new StyledRun(sb.ToString(), currentStyle, currentLink));
        return runs.ToArray();
    }

    // ===== test / renderer read helpers =====

    /// <summary>The visible-grid cell at (<paramref name="row"/>, <paramref name="col"/>).</summary>
    public Cell GetCell(int row, int col)
    {
        if ((uint)row >= (uint)_rows)
            throw new ArgumentOutOfRangeException(nameof(row));
        if ((uint)col >= (uint)_cols)
            throw new ArgumentOutOfRangeException(nameof(col));
        return Active[row][col];
    }

    /// <summary>Resolves the style of the visible-grid cell at (<paramref name="row"/>, <paramref name="col"/>).</summary>
    public TerminalStyle StyleAt(int row, int col) => _styles.Resolve(GetCell(row, col).StyleId);

    /// <summary>Plain text of a visible row (wide-trailing columns omitted; trailing spaces kept).</summary>
    public string GetRowText(int row)
    {
        if ((uint)row >= (uint)_rows)
            throw new ArgumentOutOfRangeException(nameof(row));
        return RowToString(Active[row]);
    }

    /// <summary>Plain text of a scrollback line (0 = oldest retained).</summary>
    public string GetScrollbackText(int index) => RowToString(_scrollback[index]);

    // ===== internals =====

    private Cell[][] Active => _altActive ? _altGrid! : _grid;

    private void SetStyle(TerminalStyle style)
    {
        _style = style;
        _styleId = _styles.Intern(style);
    }

    private Cell[] BlankRow()
    {
        var row = new Cell[_cols];
        Array.Fill(row, Cell.Blank);
        return row;
    }

    private void Reset()
    {
        _scrollback.Clear();
        _grid = NewGrid(_rows, _cols);
        _altGrid = null;
        _altActive = false;
        _cursorRow = 0;
        _cursorCol = 0;
        _savedRow = 0;
        _savedCol = 0;
        _wrapPending = false;
        SetStyle(TerminalStyle.Default);
        _linkId = 0;
        _savedStyle = TerminalStyle.Default;
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
        _autoWrap = true;
        _cursorVisible = true;
        _applicationCursorKeys = false;
        _bracketedPaste = false;
        _mouseMode = MouseTracking.Off;
        _mouseSgr = false;
        Title = string.Empty;
        _blocks.Clear();
        _openBlock = null;
        _dirtyFrom = 0;
    }

    private void MarkRowDirty(int gridRow) => MarkDirty(GridTopLine + gridRow);

    private void MarkAllDirty() => MarkDirty(GridTopLine);

    private void MarkDirty(long absoluteLine)
    {
        if (absoluteLine < _dirtyFrom)
            _dirtyFrom = absoluteLine;
    }

    private static int Amount(VtParams p, int index) => Math.Max(1, p.Get(index, 1));

    private static void BreakWideNeighbour(Cell[] row, int col)
    {
        // Overwriting one half of a wide pair leaves an orphan half — blank it.
        if (row[col].IsWideTrailing)
        {
            if (col > 0)
                row[col - 1] = Cell.Blank;
        }
        else if (col + 1 < row.Length && row[col + 1].IsWideTrailing)
        {
            row[col + 1] = Cell.Blank;
        }
    }

    private static void FillRow(Cell[] row, Cell blank)
    {
        for (int c = 0; c < row.Length; c++) row[c] = blank;
    }

    private static Cell[][] NewGrid(int rows, int cols)
    {
        var grid = new Cell[rows][];
        for (int r = 0; r < rows; r++)
        {
            var row = new Cell[cols];
            Array.Fill(row, Cell.Blank);
            grid[r] = row;
        }
        return grid;
    }

    private static string RowToString(Cell[] cells)
    {
        var sb = new StringBuilder(cells.Length);
        foreach (var cell in cells)
        {
            if (cell.IsWideTrailing)
                continue;
            sb.Append(cell.Rune.ToString());
        }
        return sb.ToString();
    }
}
