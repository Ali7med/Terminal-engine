using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TerminalLauncher.Terminal;

namespace TerminalLauncher.Theme;

/// <summary>
/// نظام ثيم مُسمّى (Presets) على طراز Warp: كل ثيم حزمة متكاملة (وضع + خلفيّات + لون تمييز + معاينة
/// أربعة ألوان). يبدّل فراشي الموارد (DynamicResource) وقت التشغيل عبر <see cref="Apply"/>.
/// </summary>
public static class ThemeManager
{
    /// <summary>ثيم مُسمّى: حزمة ألوان كاملة + أربعة ألوان معاينة تظهر في بطاقة الاختيار.</summary>
    public sealed record ThemePreset(
        string Id, string NameAr, string NameEn, ThemeMode Mode,
        Color Bg, Color TerminalBg, Color Surface, Color Surface2, Color SurfaceHover,
        Color Border, Color Text, Color TextMuted, Color Accent,
        Color[] Swatches);

    public static readonly ThemePreset[] Presets =
    {
        // دارك محايد دافئ (الافتراضي — قريب من واجهة كلود).
        new("helium-dark", "هيليوم داكن", "Helium Dark", ThemeMode.Dark,
            C(0x1A,0x1A,0x1A), C(0x1A,0x1A,0x1A), C(0x29,0x28,0x26), C(0x33,0x32,0x30), C(0x3D,0x3B,0x38),
            C(0x3A,0x39,0x36), C(0xEC,0xEA,0xE6), C(0x9D,0x99,0x8F), C(0xC9,0x64,0x42),
            new[]{ C(0xF7,0x76,0x8E), C(0x9E,0xCE,0x6A), C(0x7A,0xA2,0xF7), C(0xE0,0xAF,0x68) }),

        // أزرق ليليّ بارد.
        new("midnight", "منتصف الليل", "Midnight", ThemeMode.Dark,
            C(0x16,0x18,0x1F), C(0x12,0x14,0x20), C(0x1E,0x21,0x30), C(0x26,0x2A,0x3D), C(0x2F,0x34,0x50),
            C(0x2A,0x2E,0x42), C(0xE6,0xE9,0xF2), C(0x8A,0x90,0xA8), C(0x7A,0xA2,0xF7),
            new[]{ C(0x7D,0xCF,0xFF), C(0xBB,0x9A,0xF7), C(0x9E,0xCE,0x6A), C(0xF7,0x76,0x8E) }),

        // محيط سماويّ/أخضر.
        new("ocean", "محيط", "Ocean", ThemeMode.Dark,
            C(0x10,0x1E,0x1D), C(0x0C,0x18,0x17), C(0x18,0x30,0x2E), C(0x20,0x40,0x3D), C(0x2A,0x52,0x4E),
            C(0x24,0x48,0x44), C(0xE0,0xF0,0xEE), C(0x86,0xA6,0xA0), C(0x2A,0xC3,0xDE),
            new[]{ C(0x7D,0xE3,0xD0), C(0x9E,0xCE,0x6A), C(0x5F,0xB3,0xC9), C(0xE0,0xAF,0x68) }),

        // جمشت بنفسجيّ.
        new("amethyst", "جمشت", "Amethyst", ThemeMode.Dark,
            C(0x1A,0x17,0x25), C(0x15,0x12,0x1F), C(0x24,0x1F,0x33), C(0x2E,0x28,0x42), C(0x38,0x30,0x52),
            C(0x2F,0x28,0x48), C(0xE9,0xE4,0xF5), C(0x96,0x8F,0xB0), C(0xBB,0x9A,0xF7),
            new[]{ C(0xC9,0xA0,0xF7), C(0xF7,0x9A,0xD0), C(0x9E,0xCE,0x6A), C(0x7A,0xA2,0xF7) }),

        // أديبيري — باستيل هادئ على داكن.
        new("adeberry", "أديبيري", "Adeberry", ThemeMode.Dark,
            C(0x1C,0x1A,0x1D), C(0x17,0x15,0x18), C(0x26,0x23,0x29), C(0x32,0x2E,0x36), C(0x3C,0x37,0x42),
            C(0x39,0x34,0x3E), C(0xE8,0xE4,0xEC), C(0x9E,0x97,0xA6), C(0xC3,0x9A,0xBF),
            new[]{ C(0xC9,0x9A,0xA0), C(0x9C,0xCB,0xA8), C(0x9F,0xB0,0xDC), C(0xD3,0xC3,0xA0) }),

        // ===== ثيمات إضافيّة (على غرار Warp) =====

        // فينومينون — داكن بلكنة ماجنتا.
        new("phenomenon", "فينومينون", "Phenomenon", ThemeMode.Dark,
            C(0x17,0x13,0x1C), C(0x14,0x0F,0x18), C(0x21,0x1B,0x29), C(0x2C,0x24,0x36), C(0x36,0x2C,0x42),
            C(0x2C,0x24,0x36), C(0xE8,0xE1,0xF0), C(0x94,0x8B,0xA6), C(0xE0,0x52,0xA0),
            new[]{ C(0xE0,0x52,0xA0), C(0x9E,0x7C,0xE0), C(0x6A,0xD0,0xCE), C(0xE0,0xAF,0x68) }),

        // أسود صرف — لكنة سماويّة.
        new("dark", "أسود", "Dark", ThemeMode.Dark,
            C(0x0A,0x0A,0x0A), C(0x00,0x00,0x00), C(0x14,0x14,0x14), C(0x1E,0x1E,0x1E), C(0x2A,0x2A,0x2A),
            C(0x26,0x26,0x26), C(0xE6,0xE6,0xE6), C(0x7A,0x7A,0x7A), C(0x29,0xB6,0xF6),
            new[]{ C(0x7A,0xA2,0xF7), C(0xF7,0x76,0x8E), C(0x9E,0xCE,0x6A), C(0xE0,0xAF,0x68) }),

        // دراكولا — الكلاسيكيّ.
        new("dracula", "دراكولا", "Dracula", ThemeMode.Dark,
            C(0x28,0x2A,0x36), C(0x28,0x2A,0x36), C(0x34,0x37,0x46), C(0x44,0x47,0x5A), C(0x4E,0x51,0x67),
            C(0x44,0x47,0x5A), C(0xF8,0xF8,0xF2), C(0x62,0x72,0xA4), C(0xBD,0x93,0xF9),
            new[]{ C(0xFF,0x79,0xC6), C(0x50,0xFA,0x7B), C(0x8B,0xE9,0xFD), C(0xFF,0xB8,0x6C) }),

        // دراكولا فاخر — لكنة ورديّة على أسطح أفتح.
        new("fancy-dracula", "دراكولا فاخر", "Fancy Dracula", ThemeMode.Dark,
            C(0x2A,0x2C,0x3A), C(0x26,0x28,0x35), C(0x36,0x39,0x48), C(0x45,0x48,0x63), C(0x4F,0x52,0x70),
            C(0x45,0x48,0x63), C(0xF8,0xF8,0xF2), C(0x70,0x80,0xB0), C(0xFF,0x79,0xC6),
            new[]{ C(0xBD,0x93,0xF9), C(0x8B,0xE9,0xFD), C(0x50,0xFA,0x7B), C(0xFF,0xB8,0x6C) }),

        // موجة سايبر — نيون سماويّ على داكن.
        new("cyber-wave", "موجة سايبر", "Cyber Wave", ThemeMode.Dark,
            C(0x0A,0x15,0x20), C(0x07,0x10,0x18), C(0x10,0x20,0x2E), C(0x16,0x2C,0x3D), C(0x1E,0x3A,0x4F),
            C(0x16,0x2C,0x3D), C(0xD6,0xEC,0xF5), C(0x7C,0x96,0xA6), C(0x22,0xD3,0xEE),
            new[]{ C(0x22,0xD3,0xEE), C(0x38,0xBD,0xF8), C(0xA7,0x8B,0xFA), C(0xF4,0x72,0xB6) }),

        // توهّج شمسيّ — دافئ كهرمانيّ على داكن.
        new("solar-flare", "توهّج شمسيّ", "Solar Flare", ThemeMode.Dark,
            C(0x1A,0x14,0x10), C(0x15,0x10,0x0C), C(0x26,0x20,0x1A), C(0x32,0x2A,0x22), C(0x3E,0x34,0x2A),
            C(0x32,0x2A,0x22), C(0xF0,0xE6,0xDC), C(0xA6,0x96,0x8A), C(0xF5,0x9E,0x0B),
            new[]{ C(0xF5,0x9E,0x0B), C(0xFB,0x92,0x3C), C(0xF8,0x71,0x71), C(0xFB,0xBF,0x24) }),

        // سولاريزد داكن.
        new("solarized-dark", "سولاريزد داكن", "Solarized Dark", ThemeMode.Dark,
            C(0x00,0x2B,0x36), C(0x00,0x2B,0x36), C(0x07,0x36,0x42), C(0x0A,0x40,0x4D), C(0x0E,0x4B,0x59),
            C(0x0A,0x3C,0x48), C(0x93,0xA1,0xA1), C(0x58,0x6E,0x75), C(0x26,0x8B,0xD2),
            new[]{ C(0x2A,0xA1,0x98), C(0x85,0x99,0x00), C(0xB5,0x89,0x00), C(0xD3,0x36,0x82) }),

        // حلم الصفصاف — أخضر مزرقّ.
        new("willow-dream", "حلم الصفصاف", "Willow Dream", ThemeMode.Dark,
            C(0x0C,0x24,0x22), C(0x0A,0x1E,0x1C), C(0x14,0x34,0x30), C(0x1C,0x44,0x3E), C(0x24,0x56,0x50),
            C(0x1C,0x44,0x3E), C(0xDC,0xF0,0xEC), C(0x86,0xA6,0xA0), C(0x2D,0xD4,0xBF),
            new[]{ C(0x2D,0xD4,0xBF), C(0x5E,0xEA,0xD4), C(0x9E,0xCE,0x6A), C(0xE0,0xAF,0x68) }),

        // مدينة داكنة — رماديّ أزرق بلكنة حمراء.
        new("dark-city", "مدينة داكنة", "Dark City", ThemeMode.Dark,
            C(0x10,0x14,0x1A), C(0x0C,0x10,0x15), C(0x1A,0x20,0x28), C(0x24,0x2C,0x36), C(0x2E,0x38,0x44),
            C(0x24,0x2C,0x36), C(0xDC,0xE4,0xEC), C(0x88,0x94,0xA2), C(0xF4,0x3F,0x5E),
            new[]{ C(0xF4,0x3F,0x5E), C(0x38,0xBD,0xF8), C(0xA3,0xA3,0xA3), C(0xE0,0xAF,0x68) }),

        // غروڤبوكس داكن.
        new("gruvbox-dark", "غروڤبوكس داكن", "Gruvbox Dark", ThemeMode.Dark,
            C(0x28,0x28,0x28), C(0x1D,0x20,0x21), C(0x32,0x30,0x2F), C(0x3C,0x38,0x36), C(0x50,0x49,0x45),
            C(0x3C,0x38,0x36), C(0xEB,0xDB,0xB2), C(0x92,0x83,0x74), C(0xFE,0x80,0x19),
            new[]{ C(0xFB,0x49,0x34), C(0xB8,0xBB,0x26), C(0xFA,0xBD,0x2F), C(0x83,0xA5,0x98) }),

        // صخرة حمراء — دافئ ترابيّ.
        new("red-rock", "صخرة حمراء", "Red Rock", ThemeMode.Dark,
            C(0x1C,0x12,0x10), C(0x17,0x0E,0x0C), C(0x2A,0x1C,0x18), C(0x38,0x26,0x22), C(0x46,0x30,0x2A),
            C(0x38,0x26,0x22), C(0xF0,0xE0,0xDC), C(0xA6,0x8A,0x82), C(0xE0,0x6A,0x4A),
            new[]{ C(0xE0,0x6A,0x4A), C(0xE0,0x91,0x6A), C(0xC9,0x9A,0x9A), C(0x9C,0xCB,0xA8) }),

        // قنديل البحر — أزرق بنفسجيّ.
        new("jellyfish", "قنديل البحر", "Jellyfish", ThemeMode.Dark,
            C(0x0E,0x12,0x20), C(0x0A,0x0E,0x1A), C(0x17,0x1C,0x2E), C(0x20,0x26,0x3C), C(0x2A,0x32,0x4C),
            C(0x20,0x26,0x3C), C(0xDE,0xE2,0xF0), C(0x88,0x90,0xA8), C(0x60,0xA5,0xFA),
            new[]{ C(0x60,0xA5,0xFA), C(0xA7,0x8B,0xFA), C(0xF4,0x72,0xB6), C(0x34,0xD3,0x99) }),

        // أوراق — أخضر غابيّ.
        new("leafy", "أوراق", "Leafy", ThemeMode.Dark,
            C(0x0E,0x1A,0x0E), C(0x0A,0x15,0x0A), C(0x18,0x28,0x18), C(0x20,0x34,0x20), C(0x2A,0x42,0x2A),
            C(0x20,0x34,0x20), C(0xE0,0xF0,0xDC), C(0x90,0xA6,0x88), C(0x6A,0xBE,0x4A),
            new[]{ C(0x6A,0xBE,0x4A), C(0x9E,0xCE,0x6A), C(0xB8,0xD9,0x6A), C(0xE0,0xAF,0x68) }),

        // كوي — داكن بلكنة حمراء برتقاليّة.
        new("koi", "كوي", "Koi", ThemeMode.Dark,
            C(0x14,0x10,0x0E), C(0x10,0x0C,0x0A), C(0x20,0x1A,0x16), C(0x2C,0x24,0x1E), C(0x38,0x2E,0x26),
            C(0x2C,0x24,0x1E), C(0xF0,0xE8,0xE0), C(0xA6,0x96,0x8A), C(0xEF,0x44,0x44),
            new[]{ C(0xEF,0x44,0x44), C(0xF9,0x73,0x16), C(0xE0,0xAF,0x68), C(0xDC,0xDC,0xDC) }),

        // فاتح دافئ — سطح تيرمنال فاتح (كان داكناً فيتعارض مع كون الثيم فاتحاً ويجعل الكتابة الفاتحة غير مقروءة).
        new("helium-light", "هيليوم فاتح", "Helium Light", ThemeMode.Light,
            C(0xF7,0xF6,0xF3), C(0xFC,0xFB,0xF8), C(0xFF,0xFF,0xFF), C(0xEF,0xED,0xE8), C(0xE6,0xE3,0xDC),
            C(0xDD,0xD9,0xD1), C(0x2A,0x28,0x24), C(0x6E,0x6A,0x62), C(0xC9,0x64,0x42),
            new[]{ C(0xC4,0x3E,0x6B), C(0x2E,0x7D,0x57), C(0x15,0x60,0x7A), C(0x8A,0x7A,0x12) }),

        // فاتح صرف — أبيض بلكنة سماويّة.
        new("light", "فاتح", "Light", ThemeMode.Light,
            C(0xFF,0xFF,0xFF), C(0xFF,0xFF,0xFF), C(0xF4,0xF4,0xF5), C(0xE9,0xE9,0xEB), C(0xDE,0xDE,0xE0),
            C(0xE4,0xE4,0xE7), C(0x1F,0x29,0x37), C(0x6B,0x72,0x80), C(0x06,0xB6,0xD4),
            new[]{ C(0x06,0xB6,0xD4), C(0x3B,0x82,0xF6), C(0x10,0xB9,0x81), C(0xF5,0x9E,0x0B) }),

        // سولاريزد فاتح.
        new("solarized-light", "سولاريزد فاتح", "Solarized Light", ThemeMode.Light,
            C(0xFD,0xF6,0xE3), C(0xFD,0xF6,0xE3), C(0xEE,0xE8,0xD5), C(0xE4,0xDC,0xC4), C(0xD9,0xD2,0xBA),
            C(0xDD,0xD6,0xC1), C(0x58,0x6E,0x75), C(0x93,0xA1,0xA1), C(0x26,0x8B,0xD2),
            new[]{ C(0x2A,0xA1,0x98), C(0x85,0x99,0x00), C(0xCB,0x4B,0x16), C(0xD3,0x36,0x82) }),

        // غروڤبوكس فاتح.
        new("gruvbox-light", "غروڤبوكس فاتح", "Gruvbox Light", ThemeMode.Light,
            C(0xFB,0xF1,0xC7), C(0xFB,0xF1,0xC7), C(0xF2,0xE5,0xBC), C(0xEB,0xDB,0xB2), C(0xD5,0xC4,0xA1),
            C(0xD5,0xC4,0xA1), C(0x3C,0x38,0x36), C(0x7C,0x6F,0x64), C(0xAF,0x3A,0x03),
            new[]{ C(0x9D,0x00,0x06), C(0x79,0x74,0x0E), C(0x07,0x66,0x78), C(0xB5,0x76,0x14) }),

        // ثلجيّ — فاتح باردٌ مزرقّ.
        new("snowy", "ثلجيّ", "Snowy", ThemeMode.Light,
            C(0xEE,0xF2,0xF6), C(0xE8,0xEE,0xF4), C(0xFF,0xFF,0xFF), C(0xE4,0xEA,0xF0), C(0xD6,0xDE,0xE6),
            C(0xD6,0xDE,0xE6), C(0x2A,0x36,0x40), C(0x6A,0x7A,0x88), C(0x3B,0x82,0xC4),
            new[]{ C(0x3B,0x82,0xC4), C(0x5F,0xA8,0xD3), C(0x2E,0x7D,0x57), C(0xC4,0x3E,0x6B) }),

        // مدينة ورديّة — فاتح ورديّ.
        new("pink-city", "مدينة ورديّة", "Pink City", ThemeMode.Light,
            C(0xFB,0xEE,0xF4), C(0xFB,0xEE,0xF4), C(0xFF,0xFF,0xFF), C(0xF5,0xE0,0xEA), C(0xED,0xD0,0xDE),
            C(0xED,0xD0,0xDE), C(0x3A,0x2A,0x32), C(0x8A,0x6A,0x78), C(0xEC,0x48,0x99),
            new[]{ C(0xEC,0x48,0x99), C(0xF4,0x72,0xB6), C(0xA7,0x8B,0xFA), C(0x60,0xA5,0xFA) }),

        // رخام — فاتح رماديّ محايد.
        new("marble", "رخام", "Marble", ThemeMode.Light,
            C(0xF0,0xF0,0xF2), C(0xEC,0xEC,0xEE), C(0xFF,0xFF,0xFF), C(0xE6,0xE6,0xE8), C(0xDA,0xDA,0xDC),
            C(0xDA,0xDA,0xDC), C(0x2A,0x2A,0x2E), C(0x6E,0x6E,0x74), C(0x64,0x74,0x8B),
            new[]{ C(0x64,0x74,0x8B), C(0x94,0xA3,0xB8), C(0x47,0x55,0x69), C(0xA1,0x62,0x07) }),
    };

    public static Color BackgroundColor { get; private set; } = Presets[0].Bg;
    public static Color TerminalBackground { get; private set; } = Presets[0].TerminalBg;

    /// <summary>يُرجِع الثيم بالمعرّف، أو الافتراضي إن لم يوجد.</summary>
    public static ThemePreset Resolve(string? id)
        => Presets.FirstOrDefault(p => p.Id == id) ?? Presets[0];

    /// <summary>معرّف الثيم الافتراضي الموافق للوضع (يُستعمل في تبديل المظهر السريع/مزامنة النظام).</summary>
    public static string DefaultFor(ThemeMode mode)
        => mode == ThemeMode.Light ? "helium-light" : "helium-dark";

    public static void Apply(AppSettings s)
    {
        var p = Resolve(s.ThemePresetId);
        s.Mode = p.Mode;   // يبقى Mode متزامناً مع الثيم لأجل التبديل السريع.

        var r = Application.Current.Resources;

        BackgroundColor    = p.Bg;
        TerminalBackground = p.TerminalBg;

        Set(r, "Brush.Bg",           p.Bg);
        Set(r, "Brush.TerminalBg",   p.TerminalBg);
        Set(r, "Brush.Surface",      p.Surface);
        Set(r, "Brush.Surface2",     p.Surface2);
        Set(r, "Brush.SurfaceHover", p.SurfaceHover);
        Set(r, "Brush.Border",       p.Border);
        Set(r, "Brush.Text",         p.Text);
        Set(r, "Brush.TextMuted",    p.TextMuted);

        // لون النصّ فوق لون التمييز يُختار حسب سطوعه (داكن على الفاتح، أبيض على الغامق).
        double lum = (0.299 * p.Accent.R + 0.587 * p.Accent.G + 0.114 * p.Accent.B) / 255.0;
        Set(r, "Brush.OnAccent", lum > 0.6 ? C(0x1A, 0x19, 0x17) : C(0xFF, 0xFF, 0xFF));

        // طبقة تعتيم لشريط رأس التيرمنال: شبه شفّافة بلون سطح الثيم كي يبقى نصّ الشريط مقروءاً فوق
        // صورة الخلفيّة (داكنة والكتابة فاتحة في الوضع الداكن، فاتحة والكتابة داكنة في الفاتح).
        byte scrimA = p.Mode == ThemeMode.Light ? (byte)0xDE : (byte)0xC8;
        Set(r, "Brush.HeaderScrim", Freeze(new SolidColorBrush(
            Color.FromArgb(scrimA, p.Surface.R, p.Surface.G, p.Surface.B))));

        // سطح شبه شفّاف قليلاً — لخلفيّة لوحة الإعدادات (يظهر ما خلفها بخفوت).
        Set(r, "Brush.SurfaceGlass", Freeze(new SolidColorBrush(
            Color.FromArgb(0xEB, p.Surface.R, p.Surface.G, p.Surface.B))));

        Set(r, "Brush.Accent",      p.Accent);
        Set(r, "Brush.AccentHover", Shade(p.Accent, p.Mode == ThemeMode.Dark ? 0.12 : -0.10));
        Set(r, "Brush.AccentSoft",  new SolidColorBrush(p.Accent) { Opacity = p.Mode == ThemeMode.Dark ? 0.22 : 0.16 });
        Set(r, "Brush.Danger",      C(0xE0, 0x60, 0x3F));
        Set(r, "Brush.Success",     C(0x9E, 0xCE, 0x6A));

        // لوحة ANSI تتبع الثيم: خلفيّة SGR المعكوس = خلفيّة التيرمنال، والأساس 0..15 يُحسَّن للوضع
        // (ألوان أغمق على الفاتح كي تُقرأ)، ولون الكتابة الافتراضيّ يتباين مع خلفيّة التيرمنال.
        AnsiPalette.BackgroundColor = TerminalBackground;
        AnsiPalette.UseLightBase(p.Mode == ThemeMode.Light);
        AnsiPalette.DefaultForeground = ResolveTerminalForeground(s);
    }

    /// <summary>
    /// لون الكتابة الافتراضيّ للتيرمنال (النصّ بلا SGR): يحترم اختيار المستخدم الصريح، وإلّا يختار تلقائياً
    /// لوناً يتباين مع خلفيّة التيرمنال (فاتح على الداكنة، داكن على الفاتحة). القيمة الفارغة أو "auto" أو
    /// الافتراضيّ القديم "#D4D4D4" تُعامَل كـ«تلقائيّ» كي تستفيد الإصدارات القديمة من التباين الصحيح.
    /// </summary>
    public static Color ResolveTerminalForeground(AppSettings s)
    {
        var p = Resolve(s.ThemePresetId);
        Color bg = p.TerminalBg;
        Color contrast = Luminance(bg) > 0.5 ? p.Text : C(0xD4, 0xD4, 0xD4); // داكن على الفاتح، فاتح على الداكن

        string fg = (s.DefaultForeground ?? "").Trim();
        bool auto = fg.Length == 0
                 || fg.Equals("auto", StringComparison.OrdinalIgnoreCase)
                 || fg.Equals("#D4D4D4", StringComparison.OrdinalIgnoreCase);
        if (auto) return contrast;

        Color chosen;
        try { chosen = (Color)ColorConverter.ConvertFromString(fg); }
        catch { return contrast; }   // لون غير صالح ⇒ تلقائيّ

        // حارس القراءة: مهما اختار المستخدم، لا نرسم نصّاً غير مقروء فوق خلفيّة التيرمنال. إن ضعُف التباين
        // (كالنصّ الفاتح فوق ثيم فاتح) نرتدّ للّون المتباين تلقائياً — يضمن وضوح الكتابة في كلّ الثيمات.
        return ContrastRatio(chosen, bg) < 2.5 ? contrast : chosen;
    }

    /// <summary>الإضاءة النسبيّة (sRGB مبسّط 0..1) للون — تُستعمل لاختيار لون كتابة متباين.</summary>
    private static double Luminance(Color c)
        => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    /// <summary>نسبة تباين تقريبيّة (1..21) بين لونين اعتماداً على إضاءتيهما النسبيّتين.</summary>
    private static double ContrastRatio(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static void Set(ResourceDictionary r, string key, Color c) => r[key] = new SolidColorBrush(c);
    private static void Set(ResourceDictionary r, string key, Brush b) => r[key] = b;
    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    // ===== كتالوج قوالب الخلفيّة (ألوان مصمتة + تدرّجات + نقوش) =====

    /// <summary>نوع قالب الخلفيّة (يطابق <see cref="AppSettings.BackgroundKind"/> عدا "theme"/"image").</summary>
    public enum BackgroundTemplateKind { Solid, Gradient, Pattern }

    /// <summary>
    /// قالب خلفيّة مضمّن: معرّف + اسم مترجَم + نوع + مصنع فرشاة (يُنتِج فرشاة مجمَّدة قابلة للمعاينة والتطبيق).
    /// </summary>
    public sealed record BackgroundTemplate(
        string Id, string NameAr, string NameEn, BackgroundTemplateKind Kind, Func<Brush> BrushFactory)
    {
        /// <summary>قيمة <see cref="AppSettings.BackgroundKind"/> الموافقة لهذا القالب.</summary>
        public string SettingKind => Kind switch
        {
            BackgroundTemplateKind.Solid => "solid",
            BackgroundTemplateKind.Gradient => "gradient",
            _ => "pattern",
        };

        /// <summary>قيمة <see cref="AppSettings.BackgroundValue"/>: للمصمت المعرّف=hex، ولغيره معرّف القالب.</summary>
        public string SettingValue => Id;

        public Brush CreateBrush() => BrushFactory();
    }

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    /// <summary>ألوان مصمتة داكنة أنيقة (المعرّف = hex كي يُحفَظ في BackgroundValue مباشرةً).</summary>
    private static readonly (string Hex, string Ar, string En)[] SolidBackgrounds =
    {
        ("#101012", "فحميّ",   "Charcoal"),
        ("#0F1419", "لازورد",  "Deep Navy"),
        ("#12161B", "إردوازيّ","Slate"),
        ("#171417", "باذنجانيّ","Plum"),
        ("#101A16", "حرجيّ",   "Forest"),
    };

    public static readonly BackgroundTemplate[] BackgroundTemplates = BuildBackgroundTemplates();

    private static BackgroundTemplate[] BuildBackgroundTemplates()
    {
        var list = new System.Collections.Generic.List<BackgroundTemplate>();

        // --- ألوان مصمتة ---
        foreach (var (hex, ar, en) in SolidBackgrounds)
        {
            string h = hex;
            list.Add(new BackgroundTemplate(h, ar, en, BackgroundTemplateKind.Solid,
                () => Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)))));
        }

        // --- تدرّجات ---
        list.Add(new BackgroundTemplate("grad-midnight", "شفق ليليّ", "Midnight Aurora",
            BackgroundTemplateKind.Gradient,
            () => LinearGrad(new(45, 90), (0.0, "#0B1026"), (1.0, "#241533"))));
        list.Add(new BackgroundTemplate("grad-ember", "جمر", "Ember",
            BackgroundTemplateKind.Gradient,
            () => LinearGrad(new(0, 0), (0.0, "#1A1210"), (1.0, "#2A1510"))));
        list.Add(new BackgroundTemplate("grad-ocean", "غور المحيط", "Deep Ocean",
            BackgroundTemplateKind.Gradient,
            () => LinearGrad(new(0, 45), (0.0, "#081A1E"), (1.0, "#0E2833"))));
        list.Add(new BackgroundTemplate("grad-violet", "بنفسج", "Violet Dusk",
            BackgroundTemplateKind.Gradient,
            () => LinearGrad(new(30, 90), (0.0, "#14101F"), (0.55, "#1E1633"), (1.0, "#2A1030"))));

        // --- نقوش (DrawingBrush مبلَّط) ---
        list.Add(new BackgroundTemplate("pat-dots", "نقاط", "Dots",
            BackgroundTemplateKind.Pattern,
            () => DotsPattern("#12141A", "#2A2F3A")));
        list.Add(new BackgroundTemplate("pat-grid", "شبكة", "Grid",
            BackgroundTemplateKind.Pattern,
            () => GridPattern("#101216", "#22262E")));
        list.Add(new BackgroundTemplate("pat-diagonal", "أسطر مائلة", "Diagonal",
            BackgroundTemplateKind.Pattern,
            () => DiagonalPattern("#121016", "#242030")));

        return list.ToArray();
    }

    /// <summary>يُرجِع القالب بالمعرّف، أو null إن لم يوجد.</summary>
    public static BackgroundTemplate? ResolveBackground(string? id)
        => BackgroundTemplates.FirstOrDefault(t => t.Id == id);

    private readonly record struct GradAngle(double X, double Y);

    /// <summary>تدرّج خطّيّ مجمَّد من نقاط توقّف (نسبة، hex)؛ الاتجاه تقريبيّ عبر إزاحة بسيطة.</summary>
    private static LinearGradientBrush LinearGrad(GradAngle angle, params (double Offset, string Hex)[] stops)
    {
        // اتجاه بسيط: X=زاوية أفقيّة، Y=زاوية رأسيّة (0..90) نحوّلها لنقطتَي بداية/نهاية.
        double sx = angle.X > 45 ? 1 : 0;
        double sy = angle.Y > 45 ? 0 : (angle.Y > 0 ? 0.15 : 0);
        var b = new LinearGradientBrush
        {
            StartPoint = new Point(sx, sy),
            EndPoint = new Point(1 - sx, 1),
        };
        foreach (var (offset, hex) in stops)
            b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hex), offset));
        b.Freeze();
        return b;
    }

    /// <summary>نقش نقاط: خلفيّة مصمتة + نقطة صغيرة مكرّرة على بلاطة.</summary>
    private static DrawingBrush DotsPattern(string bgHex, string dotHex)
    {
        var bg = (Color)ColorConverter.ConvertFromString(bgHex);
        var dot = (Color)ColorConverter.ConvertFromString(dotHex);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(bg),
            null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(dot),
            null, new EllipseGeometry(new Point(4, 4), 1.5, 1.5)));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 20, 20),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>نقش شبكة: خطوط رفيعة أفقيّة/رأسيّة على بلاطة.</summary>
    private static DrawingBrush GridPattern(string bgHex, string lineHex)
    {
        var bg = (Color)ColorConverter.ConvertFromString(bgHex);
        var line = (Color)ColorConverter.ConvertFromString(lineHex);
        var pen = new Pen(new SolidColorBrush(line), 1);
        pen.Freeze();
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(bg),
            null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        // خطّ سفليّ + خطّ يمنى يكوّنان الشبكة عند التبليط.
        group.Children.Add(new GeometryDrawing(null, pen,
            new LineGeometry(new Point(0, 24), new Point(24, 24))));
        group.Children.Add(new GeometryDrawing(null, pen,
            new LineGeometry(new Point(24, 0), new Point(24, 24))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 24, 24),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>نقش أسطر مائلة: خطّ قطريّ رفيع مكرّر.</summary>
    private static DrawingBrush DiagonalPattern(string bgHex, string lineHex)
    {
        var bg = (Color)ColorConverter.ConvertFromString(bgHex);
        var line = (Color)ColorConverter.ConvertFromString(lineHex);
        var pen = new Pen(new SolidColorBrush(line), 1.4);
        pen.Freeze();
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(bg),
            null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(null, pen,
            new LineGeometry(new Point(0, 16), new Point(16, 0))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>amt&gt;0 يُفتّح، amt&lt;0 يُغمّق.</summary>
    private static Color Shade(Color c, double amt)
    {
        double up = amt > 0 ? amt : 0;
        double dn = amt < 0 ? -amt : 0;
        byte Ch(byte v) => (byte)Math.Clamp(v + (255 - v) * up - v * dn, 0, 255);
        return Color.FromRgb(Ch(c.R), Ch(c.G), Ch(c.B));
    }
}
