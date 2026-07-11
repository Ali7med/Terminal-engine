namespace Terminal.Core.Vt;

/// <summary>
/// Applies an SGR (<c>CSI … m</c>) parameter list to a <see cref="TerminalStyle"/>.
/// Supports the standard attributes, the 16 base colours (normal + bright), the 256-colour
/// palette (<c>38;5;n</c>) and 24-bit true colour (<c>38;2;r;g;b</c>) in both the legacy
/// semicolon form and the modern colon sub-parameter form.
/// </summary>
public static class SgrProcessor
{
    public static TerminalStyle Apply(TerminalStyle style, VtParams p)
    {
        // "CSI m" with no parameters is a full reset.
        if (p.Count == 0)
            return TerminalStyle.Default;

        var fg = style.Foreground;
        var bg = style.Background;
        var flags = style.Flags;

        for (int i = 0; i < p.Count; i++)
        {
            int code = p.Get(i, 0);
            switch (code)
            {
                case 0: fg = AnsiColor.Default; bg = AnsiColor.Default; flags = TextStyleFlags.None; break;
                case 1: flags |= TextStyleFlags.Bold; break;
                case 2: flags |= TextStyleFlags.Dim; break;
                case 3: flags |= TextStyleFlags.Italic; break;
                case 4: flags = ApplyUnderline(flags, p, i); break;
                case 5:
                case 6: flags |= TextStyleFlags.Blink; break;
                case 7: flags |= TextStyleFlags.Inverse; break;
                case 8: flags |= TextStyleFlags.Hidden; break;
                case 9: flags |= TextStyleFlags.Strikethrough; break;
                case 21: flags |= TextStyleFlags.DoubleUnderline; break;
                case 22: flags &= ~(TextStyleFlags.Bold | TextStyleFlags.Dim); break;
                case 23: flags &= ~TextStyleFlags.Italic; break;
                case 24: flags &= ~(TextStyleFlags.Underline | TextStyleFlags.DoubleUnderline); break;
                case 25: flags &= ~TextStyleFlags.Blink; break;
                case 27: flags &= ~TextStyleFlags.Inverse; break;
                case 28: flags &= ~TextStyleFlags.Hidden; break;
                case 29: flags &= ~TextStyleFlags.Strikethrough; break;

                case 38: if (TryReadColor(p, ref i, out var efg)) fg = efg; break;
                case 39: fg = AnsiColor.Default; break;
                case 48: if (TryReadColor(p, ref i, out var ebg)) bg = ebg; break;
                case 49: bg = AnsiColor.Default; break;

                default:
                    if (code >= 30 && code <= 37) fg = AnsiColor.FromPalette(code - 30);
                    else if (code >= 40 && code <= 47) bg = AnsiColor.FromPalette(code - 40);
                    else if (code >= 90 && code <= 97) fg = AnsiColor.FromPalette(code - 90 + 8);
                    else if (code >= 100 && code <= 107) bg = AnsiColor.FromPalette(code - 100 + 8);
                    // anything else: ignored
                    break;
            }
        }

        return new TerminalStyle(fg, bg, flags);
    }

    private static TextStyleFlags ApplyUnderline(TextStyleFlags flags, VtParams p, int i)
    {
        // Colon form (4:0 none, 4:1 single, 4:2 double, 4:3 curly, …). Plain "4" = single.
        if (p.SubCount(i) > 1)
        {
            int kind = p.GetSub(i, 1, 1);
            flags &= ~(TextStyleFlags.Underline | TextStyleFlags.DoubleUnderline);
            return kind switch
            {
                0 => flags,
                2 => flags | TextStyleFlags.DoubleUnderline,
                _ => flags | TextStyleFlags.Underline,
            };
        }

        return flags | TextStyleFlags.Underline;
    }

    /// <summary>
    /// Reads an extended colour after 38/48. Advances <paramref name="i"/> across the consumed
    /// semicolon parameters; the colon sub-parameter form is self-contained in one parameter.
    /// </summary>
    private static bool TryReadColor(VtParams p, ref int i, out AnsiColor color)
    {
        color = AnsiColor.Default;

        // Colon sub-parameter form, e.g. 38:5:n  or  38:2:Pi:r:g:b  or  38:2:r:g:b
        if (p.SubCount(i) > 1)
        {
            int mode = p.GetSub(i, 1, 0);
            int subs = p.SubCount(i);
            if (mode == 5)
            {
                color = AnsiColor.FromPalette(p.GetSub(i, 2, 0));
                return true;
            }
            if (mode == 2)
            {
                // The last three sub-values are r,g,b (handles both the 5- and 6-component variants).
                int r = p.GetSub(i, subs - 3, 0);
                int g = p.GetSub(i, subs - 2, 0);
                int b = p.GetSub(i, subs - 1, 0);
                color = AnsiColor.FromRgb(Clamp(r), Clamp(g), Clamp(b));
                return true;
            }
            return false;
        }

        // Legacy semicolon form: 38;5;n  or  38;2;r;g;b
        int m = p.Get(i + 1, 0);
        if (m == 5)
        {
            color = AnsiColor.FromPalette(p.Get(i + 2, 0));
            i += 2;
            return true;
        }
        if (m == 2)
        {
            color = AnsiColor.FromRgb(Clamp(p.Get(i + 2, 0)), Clamp(p.Get(i + 3, 0)), Clamp(p.Get(i + 4, 0)));
            i += 4;
            return true;
        }
        return false;
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
}
