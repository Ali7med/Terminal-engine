using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace TerminalLauncher.Services;

/// <summary>
/// إعدادات الخطوط القابلة للتخصيص (تُحفَظ/تُقرأ من <c>fonts.json</c>). كلّ حقل خطّ فارغ = الافتراضيّ.
/// </summary>
public sealed class FontSettings
{
    /// <summary>خطّ الواجهة العامّ (فارغ = الافتراضيّ Anthropic Sans/Tajawal).</summary>
    public string UiFont { get; set; } = "";
    /// <summary>الخطّ أحاديّ العرض (التيرمنال + النصوص التقنيّة). فارغ = Cascadia/Consolas.</summary>
    public string MonoFont { get; set; } = "";

    public double UiSize { get; set; } = 13;
    public double MenuSize { get; set; } = 14;
    public double TableSize { get; set; } = 12.5;
    public double SmallSize { get; set; } = 11;
    public double TitleSize { get; set; } = 16;

    public FontSettings Clone() => (FontSettings)MemberwiseClone();
}

/// <summary>
/// يدير خطوط التطبيق: يحمّل/يحفظ <c>fonts.json</c>، ويطبّق القيم على موارد التطبيق
/// (<c>Font.Ui</c>/<c>Font.Mono</c> و<c>Size.*</c>) عبر <c>DynamicResource</c> فتتحدّث الواجهة حيّاً.
/// النوافذ/التيرمنال تشترك في <see cref="Changed"/> لتحدّث ما لا يُشتَقّ من الموارد (خطّ التيرمنال).
/// </summary>
public static class FontManager
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher");

    /// <summary>مسار ملفّ إعدادات الخطوط (لفتحه يدويّاً من زرّ «فتح الملف»).</summary>
    public static string JsonPath { get; } = Path.Combine(Dir, "fonts.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static FontSettings Current { get; private set; } = new();

    /// <summary>يُطلَق بعد كلّ <see cref="Apply"/> — تشترك فيه النوافذ/التيرمنال لتحدّث خطوطها.</summary>
    public static event Action? Changed;

    // الافتراضيّات الأصليّة تُلتقَط من الموارد أوّل مرّة كي نستعيدها عند إفراغ الحقل (بلا إعادة بناء pack://).
    private static FontFamily? _defaultUi;
    private static FontFamily? _defaultMono;

    /// <summary>يقرأ الإعدادات من القرص (أو الافتراضيّ إن غاب الملف/تلِف).</summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(JsonPath))
                Current = JsonSerializer.Deserialize<FontSettings>(File.ReadAllText(JsonPath), Options) ?? new();
        }
        catch { Current = new(); }
    }

    /// <summary>يحفظ الإعدادات الحاليّة إلى <c>fonts.json</c>.</summary>
    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(JsonPath, JsonSerializer.Serialize(Current, Options));
        }
        catch { /* غير حرِج */ }
    }

    /// <summary>يستبدل الإعدادات ويحفظ ويطبّق (من لوحة الضبط).</summary>
    public static void Update(FontSettings s) { Current = s; Save(); Apply(); }

    /// <summary>يعيد قراءة الملف من القرص ثمّ يطبّقه — زرّ «تطبيق» بعد تعديل الملف يدويّاً.</summary>
    public static void ReloadAndApply() { Load(); Apply(); }

    /// <summary>يعيد كلّ القيم للافتراضيّ ويحفظ ويطبّق.</summary>
    public static void ResetToDefaults() => Update(new FontSettings());

    /// <summary>يطبّق الإعدادات على موارد التطبيق ويُطلق <see cref="Changed"/> (يُستدعى عند الإقلاع وبعد أي تعديل).</summary>
    public static void Apply()
    {
        var res = Application.Current?.Resources;
        if (res == null) return;

        _defaultUi ??= res["Font.Ui"] as FontFamily;
        _defaultMono ??= res["Font.Mono"] as FontFamily;

        var s = Current;
        res["Font.Ui"]   = FamilyOr(s.UiFont, _defaultUi);
        res["Font.Mono"] = FamilyOr(s.MonoFont, _defaultMono);
        res["Size.Ui"]    = Clamp(s.UiSize);
        res["Size.Menu"]  = Clamp(s.MenuSize);
        res["Size.Table"] = Clamp(s.TableSize);
        res["Size.Small"] = Clamp(s.SmallSize);
        res["Size.Title"] = Clamp(s.TitleSize);

        Changed?.Invoke();
    }

    /// <summary>خطّ من اسم المستخدم (مع احتياطات) أو الافتراضيّ الأصليّ إن كان فارغاً.</summary>
    private static FontFamily FamilyOr(string name, FontFamily? fallback)
        => string.IsNullOrWhiteSpace(name)
            ? fallback ?? new FontFamily("Segoe UI")
            : new FontFamily(name.Trim() + ", Segoe UI, Tahoma");

    private static double Clamp(double v) => Math.Clamp(v, 7, 40);
}
