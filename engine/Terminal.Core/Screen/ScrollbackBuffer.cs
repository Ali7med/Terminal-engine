namespace Terminal.Core.Screen;

/// <summary>
/// Fixed-capacity ring buffer of scrollback lines (guide T-004.3). Appending past capacity
/// overwrites the oldest line in O(1), so memory stays bounded no matter how much output
/// streams through (guide T-004.6). Each line is stored as its raw <see cref="Cell"/> row.
/// </summary>
public sealed class ScrollbackBuffer
{
    private readonly Cell[]?[] _lines;
    private int _start;   // ring index of the oldest retained line
    private int _count;

    public ScrollbackBuffer(int capacity)
    {
        Capacity = Math.Max(0, capacity);
        _lines = new Cell[Math.Max(1, Capacity)][];
    }

    /// <summary>Maximum retained lines. 0 disables scrollback (alternate-screen semantics).</summary>
    public int Capacity { get; }

    /// <summary>Lines currently held (0..<see cref="Capacity"/>).</summary>
    public int Count => _count;

    /// <summary>Total lines evicted over the buffer's lifetime — used for absolute line numbering.</summary>
    public long Evicted { get; private set; }

    /// <summary>Appends a line, evicting (and returning true) the oldest line when already full.</summary>
    public bool Add(Cell[] line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (Capacity == 0)
        {
            Evicted++;
            return true; // nothing retained
        }

        if (_count < Capacity)
        {
            _lines[(_start + _count) % Capacity] = line;
            _count++;
            return false;
        }

        // Full: overwrite the oldest slot and advance the ring start.
        _lines[_start] = line;
        _start = (_start + 1) % Capacity;
        Evicted++;
        return true;
    }

    /// <summary>The scrollback line at <paramref name="index"/> (0 = oldest retained).</summary>
    public Cell[] this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _lines[(_start + index) % Capacity]!;
        }
    }

    /// <summary>Drops every retained line (counting them as evicted so line numbers stay monotonic).</summary>
    public void Clear()
    {
        Evicted += _count;
        Array.Clear(_lines, 0, _lines.Length);
        _start = 0;
        _count = 0;
    }
}
