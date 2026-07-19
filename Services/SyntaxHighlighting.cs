using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace TerminalLauncher.Services;

/// <summary>
/// اختيار التلوين النحويّ لمحرّرات AvalonEdit — مصدر واحد يستعمله عارض الحاوية ومحرّر الملفّات المحلّيّ
/// (كانا يكرّران المنطق ويختلفان في احتياط JSON).
///
/// تعريفات AvalonEdit المدمجة مضبوطة لخلفيّة <b>فاتحة</b>، فألوانها الداكنة تصير غير مقروءة على الثيم
/// الداكن. لذا نُفتِّح الألوان الداكنة عند الحاجة. التعريفات كائنات مشتركة على مستوى العمليّة، فنحفظ
/// الألوان الأصليّة أوّل مرّة ونشتقّ منها دائماً — فيبقى التبديل بين الثيمات صحيحاً ولا تتراكم التفتيحات.
/// </summary>
public static class SyntaxHighlighting
{
    private static readonly Dictionary<HighlightingColor, Color?> OriginalForegrounds = new();

    private static IHighlightingDefinition? _jsonKeyValue;

    /// <summary>
    /// تعريف تلوين JSON يميّز <b>المفتاح عن القيمة</b> (تعريف JavaScript المدمج يلوّن الاثنين نصّاً واحداً).
    /// المفتاح = سلسلة يتبعها نقطتان. ألوانه مضبوطة لتُقرأ على الخلفيّة الداكنة (الوضع الافتراضيّ).
    /// يُحمَّل مرّة ويُخزَّن. يُستعمَل في محرّر الإعدادات المدمج.
    /// </summary>
    public static IHighlightingDefinition JsonKeyValue()
    {
        if (_jsonKeyValue != null) return _jsonKeyValue;
        using var reader = XmlReader.Create(new StringReader(JsonKeyValueXshd));
        _jsonKeyValue = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        return _jsonKeyValue;
    }

    // المفتاح أوّلاً (بحث أماميّ عن ':') كي يفوز على قاعدة السلسلة العامّة عند نفس الموضع.
    private const string JsonKeyValueXshd = """
        <SyntaxDefinition name="JsonKV" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Key"         foreground="#E6B450" />
          <Color name="String"      foreground="#8DB36B" />
          <Color name="Number"      foreground="#5EC8C0" />
          <Color name="Bool"        foreground="#6AA0D8" fontWeight="bold" />
          <Color name="Null"        foreground="#C77B9B" fontWeight="bold" />
          <Color name="Punctuation" foreground="#9D998F" />
          <RuleSet ignoreCase="false">
            <Rule color="Key">"(?:\\.|[^"\\])*"(?=\s*:)</Rule>
            <Rule color="String">"(?:\\.|[^"\\])*"</Rule>
            <Keywords color="Bool"><Word>true</Word><Word>false</Word></Keywords>
            <Keywords color="Null"><Word>null</Word></Keywords>
            <Rule color="Number">\b[+-]?[0-9]+(\.[0-9]+)?([eE][+-]?[0-9]+)?\b</Rule>
            <Rule color="Punctuation">[{}\[\],:]</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    /// <summary>تعريف التلوين المناسب لامتداد المسار، أو null إن لم يُعرَف.</summary>
    public static IHighlightingDefinition? ForPath(string path)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            var mgr = HighlightingManager.Instance;
            var def = mgr.GetDefinitionByExtension(ext);
            // JSON غير مسجّل افتراضيّاً في AvalonEdit — نلوّنه بتعريف JavaScript (أقرب بنية)
            if (def == null && ext == ".json") def = mgr.GetDefinition("JavaScript");
            if (def != null) EnsureReadable(def);
            return def;
        }
        catch { return null; }
    }

    /// <summary>يضمن تباين ألوان التعريف مع خلفيّة الثيم الحاليّة (يُفتِّح الداكن على الداكن).</summary>
    private static void EnsureReadable(IHighlightingDefinition def)
    {
        bool darkBg = IsDarkBackground();
        foreach (var c in def.NamedHighlightingColors)
        {
            if (!OriginalForegrounds.TryGetValue(c, out var original))
                OriginalForegrounds[c] = original = c.Foreground?.GetColor(null);
            if (original is not { } src) continue;

            c.Foreground = new SimpleHighlightingBrush(darkBg ? Lighten(src) : src);
        }
    }

    /// <summary>هل خلفيّة الثيم داكنة؟ (لمعان منخفض ⇒ نحتاج ألوان نصّ أفتح).</summary>
    private static bool IsDarkBackground()
    {
        if (System.Windows.Application.Current?.TryFindResource("Brush.Bg") is SolidColorBrush b)
            return Luminance(b.Color) < 0.5;
        return true;   // التطبيق داكن افتراضيّاً
    }

    /// <summary>يرفع لمعان اللون إلى حدّ مقروء على خلفيّة داكنة مع الحفاظ على تدرّجه (HSL-lite).</summary>
    private static Color Lighten(Color c)
    {
        const double MinLum = 0.62;
        double lum = Luminance(c);
        if (lum >= MinLum) return c;
        // لون شبه أسود لا تدرّج له → رمادي فاتح؛ وإلّا نُوسّع القناة نحو الحدّ المطلوب
        if (lum < 0.02) return Color.FromRgb(0xDD, 0xDD, 0xDD);
        double k = MinLum / lum;
        return Color.FromRgb(Scale(c.R, k), Scale(c.G, k), Scale(c.B, k));
    }

    private static byte Scale(byte v, double k) => (byte)System.Math.Min(255, System.Math.Round(v * k));

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
}
