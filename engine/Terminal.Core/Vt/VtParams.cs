namespace Terminal.Core.Vt;

/// <summary>
/// Parsed numeric parameters of a CSI (or SGR) sequence. Top-level parameters are
/// separated by ';'; a parameter may itself carry ':'-separated sub-parameters
/// (used by the modern SGR extended-colour form, e.g. <c>38:2::r:g:b</c>).
/// An absent or empty parameter reads back as the caller-supplied default (ECMA-48).
/// </summary>
public readonly struct VtParams
{
    private const int Empty_ = -1; // sentinel for an explicitly-empty value

    // All values, flattened. _starts[i].._starts[i+1] is the sub-value range of top-level param i.
    private readonly int[] _values;
    private readonly int[] _starts; // length = Count + 1

    private VtParams(int[] values, int[] starts)
    {
        _values = values;
        _starts = starts;
    }

    public static readonly VtParams Empty = new(Array.Empty<int>(), new[] { 0 });

    /// <summary>Number of top-level parameters.</summary>
    public int Count => _starts is null ? 0 : _starts.Length - 1;

    /// <summary>First value of top-level parameter <paramref name="index"/>, or <paramref name="defaultValue"/> if absent/empty.</summary>
    public int Get(int index, int defaultValue)
    {
        if (_starts is null || index < 0 || index >= Count)
            return defaultValue;
        int start = _starts[index];
        if (start >= _starts[index + 1])
            return defaultValue;
        int v = _values[start];
        return v == Empty_ ? defaultValue : v;
    }

    /// <summary>Number of ':'-separated sub-values in top-level parameter <paramref name="index"/>.</summary>
    public int SubCount(int index)
    {
        if (_starts is null || index < 0 || index >= Count)
            return 0;
        return _starts[index + 1] - _starts[index];
    }

    /// <summary>Sub-value <paramref name="sub"/> of parameter <paramref name="index"/>, or <paramref name="defaultValue"/> if absent/empty.</summary>
    public int GetSub(int index, int sub, int defaultValue)
    {
        if (_starts is null || index < 0 || index >= Count || sub < 0)
            return defaultValue;
        int at = _starts[index] + sub;
        if (at >= _starts[index + 1])
            return defaultValue;
        int v = _values[at];
        return v == Empty_ ? defaultValue : v;
    }

    /// <summary>
    /// Parses a raw CSI parameter string (digits, ';', ':'). Values are clamped to keep
    /// pathological input bounded; other bytes are ignored defensively.
    /// </summary>
    public static VtParams Parse(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return Empty;

        const int MaxValues = 256;
        var values = new List<int>(8);
        var starts = new List<int> { 0 };

        int current = 0;
        bool hasDigit = false;

        void FlushValue()
        {
            if (values.Count < MaxValues)
                values.Add(hasDigit ? current : Empty_);
            current = 0;
            hasDigit = false;
        }

        foreach (char ch in text)
        {
            if (ch >= '0' && ch <= '9')
            {
                if (current < 100_000_000) // overflow guard for absurd digit runs
                    current = current * 10 + (ch - '0');
                hasDigit = true;
            }
            else if (ch == ':')
            {
                FlushValue();
            }
            else if (ch == ';')
            {
                FlushValue();
                if (starts.Count <= MaxValues)
                    starts.Add(values.Count);
            }
            // any other byte is ignored (defensive; the parser strips these already)
        }

        FlushValue();
        starts.Add(values.Count);

        return new VtParams(values.ToArray(), starts.ToArray());
    }
}
