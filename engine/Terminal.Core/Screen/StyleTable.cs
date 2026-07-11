using System.Collections.Generic;
using Terminal.Core.Vt;

namespace Terminal.Core.Screen;

/// <summary>
/// Interns <see cref="TerminalStyle"/> values so each grid cell stores a small integer id
/// instead of the full style (guide T-004.1). Id 0 is always <see cref="TerminalStyle.Default"/>.
/// The set of distinct styles in a real session is small; the table only grows when a new
/// colour/attribute combination first appears.
/// </summary>
public sealed class StyleTable
{
    private readonly List<TerminalStyle> _styles = new() { TerminalStyle.Default };
    private readonly Dictionary<TerminalStyle, int> _index = new() { [TerminalStyle.Default] = 0 };

    /// <summary>Number of distinct interned styles (always ≥ 1).</summary>
    public int Count => _styles.Count;

    /// <summary>Returns the id for <paramref name="style"/>, assigning a new one on first sight.</summary>
    public int Intern(TerminalStyle style)
    {
        if (_index.TryGetValue(style, out int id))
            return id;
        id = _styles.Count;
        _styles.Add(style);
        _index[style] = id;
        return id;
    }

    /// <summary>Resolves an id back to its style (the default style for an unknown id).</summary>
    public TerminalStyle Resolve(int id) =>
        (uint)id < (uint)_styles.Count ? _styles[id] : TerminalStyle.Default;
}
