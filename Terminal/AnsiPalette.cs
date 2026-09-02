using System.Collections.Generic;
using System.Windows.Media;

namespace TerminalLauncher.Terminal;

/// <summary>
/// لوحة ألوان ANSI الكاملة (256 لون: 16 أساس + مكعّب 6×6×6 + 24 رمادي) مع كاش فراشي مُجمّدة.
/// الـ16 الأساسية مشتقّة من طيف الثيم (Tokyo Night) لتناسق الألوان مع لكنات الواجهة.
/// </summary>
public static class AnsiPalette
{
    // لون النصّ الافتراضي (بلا SGR) وخلفية التيرمنال — كلاهما ثابت لأن سطح التيرمنال داكن في كل الأوضاع.
    public static Color DefaultForeground { get; set; } = Color.FromRgb(0xD4, 0xD4, 0xD4);
    public static Color BackgroundColor  { get; set; } = Color.FromRgb(0x1A, 0x19, 0x17);

    private static readonly Color[] Table = new Color[256];

    /// <summary>
    /// نسخة الأساس 0..15 <b>حين تُستعمَل خلفيّةً</b>. اللوحة مضبوطة نصّاً فوق سطح التيرمنال، فألوانها
    /// في الوضع الداكن فاتحة — وبرنامجٌ يرسم شارة (‏<c>INFO</c>: أبيض ANSI فوق أزرق ANSI) يضع نصّاً
    /// فاتحاً فوق خلفيّة فاتحة. الحلّ: خلفيّةٌ مُزاحة نحو قطب السطح (تُغمَّق في الوضع الداكن وتُفتَّح في
    /// الفاتح)، فيبقى «أبيض فوق أزرق» كما قصده البرنامج وتقرؤه العين.
    /// </summary>
    private static readonly Color[] BgTable = new Color[16];

    private static readonly Dictionary<uint, SolidColorBrush> BrushCache = new();

    /// <summary>
    /// الأساس 0..15 لخلفيّة داكنة (Tokyo Night) — ألوان ساطعة تُقرأ جيّداً فوق سطح داكن.
    /// </summary>
    private static readonly Color[] DarkBase =
    {
        Rgb(0x15, 0x16, 0x1E), // black
        Rgb(0xF7, 0x76, 0x8E), // red
        Rgb(0x9E, 0xCE, 0x6A), // green
        Rgb(0xE0, 0xAF, 0x68), // yellow
        Rgb(0x7A, 0xA2, 0xF7), // blue
        Rgb(0xBB, 0x9A, 0xF7), // magenta
        Rgb(0x7D, 0xCF, 0xFF), // cyan
        Rgb(0xA9, 0xB1, 0xD6), // white
        Rgb(0x41, 0x48, 0x68), // bright black
        Rgb(0xFF, 0x7A, 0x93), // bright red
        Rgb(0xB9, 0xF2, 0x7C), // bright green
        Rgb(0xFF, 0x9E, 0x64), // bright yellow
        Rgb(0x7D, 0xA6, 0xFF), // bright blue
        Rgb(0xBB, 0x9A, 0xF7), // bright magenta
        Rgb(0x0D, 0xB9, 0xD7), // bright cyan
        Rgb(0xC0, 0xCA, 0xF5), // bright white
    };

    /// <summary>
    /// الأساس 0..15 لخلفيّة فاتحة — ألوان أغمق وأكثر تشبّعاً تُقرأ فوق سطح فاتح (على غرار GitHub/Solarized
    /// Light). «الأبيض» (7/15) يُخفَّض إلى رماديّ داكن كي يبقى نصّ الصدفات الذي يفترض خلفيّة داكنة مقروءاً.
    /// </summary>
    private static readonly Color[] LightBase =
    {
        Rgb(0x1A, 0x1A, 0x1A), // black
        Rgb(0xC4, 0x34, 0x1B), // red
        Rgb(0x2E, 0x7D, 0x32), // green
        Rgb(0xA1, 0x62, 0x07), // yellow (كهرمانيّ داكن)
        Rgb(0x15, 0x65, 0xC0), // blue
        Rgb(0x8E, 0x24, 0xAA), // magenta
        Rgb(0x0E, 0x74, 0x90), // cyan
        Rgb(0x3A, 0x3A, 0x3A), // white → رماديّ داكن
        Rgb(0x5A, 0x5A, 0x5A), // bright black
        Rgb(0xD3, 0x2F, 0x2F), // bright red
        Rgb(0x38, 0x8E, 0x3C), // bright green
        Rgb(0xB4, 0x5F, 0x06), // bright yellow
        Rgb(0x19, 0x76, 0xD2), // bright blue
        Rgb(0x9C, 0x27, 0xB0), // bright magenta
        Rgb(0x08, 0x91, 0xB2), // bright cyan
        Rgb(0x1A, 0x1A, 0x1A), // bright white → شبه أسود
    };

    // هل الأساس الحاليّ مُحسَّن لخلفيّة فاتحة؟ يقوده الثيم عبر <see cref="UseLightBase"/>.
    private static bool _lightBase;

    static AnsiPalette()
    {
        // 16..231: مكعّب 6×6×6 و232..255: تدرّج رمادي — مطلقة لا تتبع الثيم.
        int n = 16;
        for (int r = 0; r < 6; r++)
            for (int g = 0; g < 6; g++)
                for (int b = 0; b < 6; b++)
                    Table[n++] = Rgb(Cube(r), Cube(g), Cube(b));

        for (int i = 0; i < 24; i++)
        {
            byte v = (byte)(8 + i * 10);
            Table[232 + i] = Rgb(v, v, v);
        }

        ApplyBase(DarkBase);   // الافتراضيّ داكن (يبدّله الثيم عند الإقلاع)
    }

    /// <summary>ينسخ مجموعة الأساس 0..15 إلى الجدول ويُفرِغ كاش الفراشي كي تُعاد بلون الأساس الجديد.</summary>
    private static void ApplyBase(Color[] baseColors)
    {
        for (int i = 0; i < 16; i++)
        {
            Table[i] = baseColors[i];
            BgTable[i] = _lightBase ? Lighten(baseColors[i], BgLuminanceFloor)
                                    : Darken(baseColors[i], BgLuminanceCap);
        }
        BrushCache.Clear();
    }

    /// <summary>
    /// سقف لمعان لون الخلفيّة في الوضع الداكن. <c>0.06</c> مُشتقّ لا مُختار: «أبيض» اللوحة
    /// (<c>#A9B1D6</c>، لمعانه ‎0.446‎) يبلغ فوقه ‎4.5:1‎ بالضبط — <c>(0.446+0.05)/(0.06+0.05)</c>.
    /// </summary>
    private const double BgLuminanceCap = 0.06;

    /// <summary>
    /// أرضيّة لمعان لون الخلفيّة في الوضع الفاتح — المرآة المقابلة: «أبيض» اللوحة الفاتحة رماديّ
    /// داكن (<c>#3A3A3A</c>)، فيحتاج خلفيّةً بلمعان ‎≥0.45‎ ليقرأ فوقها.
    /// </summary>
    private const double BgLuminanceFloor = 0.45;

    /// <summary>
    /// يُغمّق اللون حتّى ينزل لمعانه إلى <paramref name="cap"/> — بضرب القنوات في معامل واحد، فتبقى
    /// درجة اللون (‏hue) وتشبّعه النسبيّ كما هما. يُترك ما كان تحت السقف أصلاً (الأسود والرماديّ الداكن).
    /// المعامل ببحث ثنائيّ لا بقانون قوّة: منحنى sRGB خطّيّ قرب الصفر فتُخطئ الصيغة المغلقة هناك.
    /// </summary>
    private static Color Darken(Color c, double cap)
    {
        if (Luminance(c) <= cap) return c;
        double lo = 0, hi = 1;
        for (int i = 0; i < 24; i++)
        {
            double mid = (lo + hi) / 2;
            if (Luminance(Scale(c, mid)) > cap) hi = mid; else lo = mid;
        }
        return Scale(c, lo);
    }

    /// <summary>يُفتّح اللون حتّى يبلغ لمعانه <paramref name="floor"/> — بالمزج نحو الأبيض (مرآة <see cref="Darken"/>).</summary>
    private static Color Lighten(Color c, double floor)
    {
        if (Luminance(c) >= floor) return c;
        double lo = 0, hi = 1;
        for (int i = 0; i < 24; i++)
        {
            double mid = (lo + hi) / 2;
            if (Luminance(Blend(c, Colors.White, mid)) < floor) lo = mid; else hi = mid;
        }
        return Blend(c, Colors.White, hi);
    }

    private static Color Scale(Color c, double k)
        => Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));

    /// <summary>
    /// يبدّل الأساس 0..15 إلى المجموعة المُحسَّنة لخلفيّة فاتحة/داكنة (يقوده <c>ThemeManager.Apply</c>
    /// حسب وضع الثيم). لا يمسّ المكعّب 6×6×6 ولا التدرّج الرماديّ.
    /// </summary>
    public static void UseLightBase(bool light)
    {
        if (_lightBase == light) return;
        _lightBase = light;
        ApplyBase(light ? LightBase : DarkBase);
    }

    private static byte Cube(int v) => (byte)(v == 0 ? 0 : v * 40 + 55);
    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    /// <summary>يحلّ لون المقدّمة إلى Color؛ العريض يُسطِّع الألوان القياسية (0..7 → 8..15).</summary>
    public static Color ResolveForeground(AnsiColor c, bool bold) => c.Kind switch
    {
        AnsiColor.ColorKind.Rgb     => Color.FromRgb(c.R, c.G, c.B),
        AnsiColor.ColorKind.Palette => Table[bold && c.Index < 8 ? c.Index + 8 : c.Index],
        _                           => DefaultForeground,
    };

    /// <summary>يحلّ لون الخلفية؛ يعيد false عند الافتراضي (شفّاف — تظهر خلفية التيرمنال).</summary>
    public static bool TryResolveBackground(AnsiColor c, out Color color)
    {
        switch (c.Kind)
        {
            case AnsiColor.ColorKind.Rgb:     color = Color.FromRgb(c.R, c.G, c.B); return true;
            // الأساس 0..15 من الجدول المُزاح (انظر BgTable). المكعّب 6×6×6 والتدرّج الرماديّ
            // مطلقان يغطّيان المدى كلّه — من يختارهما خلفيّةً يقصد قيمتهما بعينها، فلا نمسّهما.
            case AnsiColor.ColorKind.Palette:
                color = c.Index < 16 ? BgTable[c.Index] : Table[c.Index];
                return true;
            default:                          color = default;                       return false;
        }
    }

    /// <summary>مزج لونين (t=0 يعيد a، t=1 يعيد b) — يُستعمل للسمة الخافتة.</summary>
    public static Color Blend(Color a, Color b, double t)
    {
        byte Ch(byte x, byte y) => (byte)(x + (y - x) * t);
        return Color.FromRgb(Ch(a.R, b.R), Ch(a.G, b.G), Ch(a.B, b.B));
    }

    // ===== حدّ أدنى للتباين بين النصّ وخلفيّته الصريحة =====

    /// <summary>
    /// نسبة التباين الدنيا (WCAG AA للنصّ العاديّ) المفروضة حين تحمل الخليّة <b>خلفيّة صريحة</b>.
    ///
    /// <para><b>لماذا:</b> ألوان الأساس ١٦ في هذه اللوحة مُصمَّمة <b>نصّاً فوق سطح داكن</b> — أزرقها
    /// <c>#7AA2F7</c> فاتح. حين يستعملها برنامجٌ <b>خلفيّةً</b> لشارة (‏<c>INFO</c> في Laravel:
    /// أبيض ANSI فوق أزرق ANSI) يصير النصّ <c>#A9B1D6</c> فوق <c>#7AA2F7</c> — تباين <c>1.17:1</c>،
    /// أي كلمة لا تكاد تُرى. لوحاتُ الطرفيّات الكلاسيكيّة تفلت من هذا لأنّ أزرقها داكن، ولوحتنا
    /// لا. فبدل تشويه اللوحة كلّها نفرض أرضيّة تباينٍ عند الرسم — وهو ما تفعله
    /// «minimum contrast» في Windows Terminal وiTerm2.</para>
    /// </summary>
    public const double MinContrastRatio = 4.5;

    /// <summary>القناة الخطّيّة (sRGB → linear) — أساس حساب اللمعان النسبيّ.</summary>
    private static double Linear(byte channel)
    {
        double v = channel / 255.0;
        return v <= 0.03928 ? v / 12.92 : System.Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    /// <summary>اللمعان النسبيّ (WCAG) للّون.</summary>
    public static double Luminance(Color c)
        => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    /// <summary>نسبة التباين بين لونين (‏1:1 = متطابقان · 21:1 = أبيض وأسود).</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        if (la < lb) (la, lb) = (lb, la);
        return (la + 0.05) / (lb + 0.05);
    }

    /// <summary>
    /// يرفع تباين <paramref name="fg"/> فوق <paramref name="bg"/> إلى <paramref name="minRatio"/>
    /// إن قصّر عنه: يمزج النصّ نحو الطرف (أبيض أو أسود) <b>الأعلى تبايناً مع الخلفيّة</b> بخطوات
    /// صغيرة، فيبقى ما أمكن من درجة اللون الأصليّة. يعيد اللون كما هو إن كان التباين كافياً.
    ///
    /// <para>اختيار الطرف بالقياس لا باللمعان: خلفيّة <c>#7AA2F7</c> لمعانها ‎0.37‎ (تُعَدّ «داكنة»)
    /// بينما الأسود فوقها يعطي ‎8.5:1‎ والأبيض ‎2.5:1‎ — فقاعدة «الداكن ⇒ نصّ أبيض» تختار الأسوأ.</para>
    /// </summary>
    public static Color EnsureContrast(Color fg, Color bg, double minRatio = MinContrastRatio)
    {
        if (minRatio <= 1.0 || Contrast(fg, bg) >= minRatio) return fg;

        Color target = Contrast(Colors.White, bg) >= Contrast(Colors.Black, bg)
            ? Colors.White : Colors.Black;

        for (double t = 0.15; t < 1.0; t += 0.15)
        {
            Color mixed = Blend(fg, target, t);
            if (Contrast(mixed, bg) >= minRatio) return mixed;
        }
        return target;   // حتّى الطرف الخالص قد لا يبلغ النسبة فوق خلفيّة متوسّطة — وهو أفضل المتاح
    }

    /// <summary>فرشاة مُجمّدة مُكاشة لكل لون — يتجنّب تخصيص فرشاة لكل مقطع.</summary>
    public static SolidColorBrush Brush(Color c)
    {
        uint key = ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        if (!BrushCache.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(c);
            brush.Freeze();
            BrushCache[key] = brush;
        }
        return brush;
    }
}
