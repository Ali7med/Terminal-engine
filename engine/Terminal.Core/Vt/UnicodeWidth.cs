using System.Globalization;
using System.Text;

namespace Terminal.Core.Vt;

/// <summary>
/// Terminal cell width of a Unicode scalar: 0 for combining/zero-width marks, 2 for the
/// East-Asian wide / fullwidth ranges and most emoji, 1 otherwise. (Arabic letters are
/// width 1 — their right-to-left shaping is a rendering concern, not a cell-width one.)
/// </summary>
public static class UnicodeWidth
{
    public static int Measure(Rune rune)
    {
        int cp = rune.Value;

        if (cp == 0)
            return 0;
        if (cp < 0x20 || (cp >= 0x7F && cp < 0xA0)) // C0 / DEL / C1 controls
            return 0;
        if (IsZeroWidth(cp))
            return 0;
        if (IsWide(cp))
            return 2;
        return 1;
    }

    private static bool IsZeroWidth(int cp)
    {
        // Explicit zero-width formatting characters.
        if (cp == 0x200B || cp == 0x200C || cp == 0x200D || cp == 0x2060 || cp == 0xFEFF)
            return true;

        var category = CharUnicodeInfo.GetUnicodeCategory(cp);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark;
    }

    private static bool IsWide(int cp) =>
        (cp >= 0x1100 && cp <= 0x115F) || // Hangul Jamo
        cp == 0x2329 || cp == 0x232A ||
        (cp >= 0x2E80 && cp <= 0x303E) || // CJK radicals … Kangxi
        (cp >= 0x3041 && cp <= 0x33FF) || // Hiragana … CJK symbols
        (cp >= 0x3400 && cp <= 0x4DBF) || // CJK Ext A
        (cp >= 0x4E00 && cp <= 0x9FFF) || // CJK Unified
        (cp >= 0xA000 && cp <= 0xA4CF) || // Yi
        (cp >= 0xAC00 && cp <= 0xD7A3) || // Hangul syllables
        (cp >= 0xF900 && cp <= 0xFAFF) || // CJK compatibility
        (cp >= 0xFE10 && cp <= 0xFE19) || // vertical forms
        (cp >= 0xFE30 && cp <= 0xFE6F) || // CJK compatibility forms
        (cp >= 0xFF00 && cp <= 0xFF60) || // fullwidth forms
        (cp >= 0xFFE0 && cp <= 0xFFE6) ||
        (cp >= 0x1F300 && cp <= 0x1F64F) || // emoji / pictographs
        (cp >= 0x1F900 && cp <= 0x1F9FF) || // supplemental symbols
        (cp >= 0x20000 && cp <= 0x3FFFD);   // CJK Ext B+
}
