using System;
using System.Collections.Generic;

namespace Terminal.Core.Screen;

/// <summary>
/// Interns OSC 8 hyperlink target URIs so each grid cell stores a small integer id instead of
/// the full string, mirroring <see cref="StyleTable"/>. Id 0 always means "no hyperlink".
/// Hyperlinks are rare in a real session, so the table stays tiny.
/// </summary>
public sealed class LinkTable
{
    private readonly List<string> _links = new() { string.Empty };   // id 0 = no link
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    /// <summary>Number of interned slots, including the "no link" slot at id 0 (always ≥ 1).</summary>
    public int Count => _links.Count;

    /// <summary>Returns the id for <paramref name="uri"/> (0 for null/empty), assigning a new one on first sight.</summary>
    public int Intern(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
            return 0;
        if (_index.TryGetValue(uri, out int id))
            return id;
        id = _links.Count;
        _links.Add(uri);
        _index[uri] = id;
        return id;
    }

    /// <summary>Resolves an id back to its URI (null for id 0 or an unknown id).</summary>
    public string? Resolve(int id) =>
        id > 0 && id < _links.Count ? _links[id] : null;
}
