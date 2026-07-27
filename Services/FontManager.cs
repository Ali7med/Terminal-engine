using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace TerminalLauncher.Services;

/// <summary>
/// إعدادات التطبيق المحلّيّة القابلة للتخصيص (تُحفَظ/تُقرأ من <c>config.json</c>): الخطوط والأحجام
/// واستدارة الحواف. كلّ حقل خطّ فارغ = الافتراضيّ.
/// </summary>
public sealed class FontSettings
{
    /// <summary>خطّ الواجهة العامّ (فارغ = الافتراضيّ Anthropic Sans/Tajawal).</summary>
    public string UiFont { get; set; } = "";
    /// <summary>الخطّ أحاديّ العرض (التيرمنال + النصوص التقنيّة). فارغ = Cascadia/Consolas.</summary>
    public string MonoFont { get; set; } = "";

    // ===== سُلَّم الأحجام حسب الدور =====
    // كلّ نصّ في التطبيق ينتمي إلى دور واحد من هذه الأدوار، ويقرأ حجمه من مورده. لا أحجام ثابتة
    // في XAML: الحجم الثابت هو سبب أن يتغيّر نصٌّ ولا يتغيّر جاره من المنزلق نفسه.
    //
    // الافتراضيّات مضبوطة على «مريح ومقروء» لا على «مضغوط»: شاشات اليوم أكبر، والقراءة الطويلة
    // أهمّ من حشر صفوف إضافيّة. من أراد الأصغر فالمنزلقات في الإعدادات.

    /// <summary>نصّ الواجهة العامّ — الأساس الذي تُشتقّ منه الأدوار التلقائيّة.</summary>
    public double UiSize { get; set; } = 14;

    /// <summary>القوائم المنسدلة وقوائم السياق.</summary>
    public double MenuSize { get; set; } = 15;

    /// <summary>صفوف الجداول والقوائم الطويلة.</summary>
    public double TableSize { get; set; } = 13.5;

    /// <summary>عناوين النوافذ والأقسام الكبرى.</summary>
    public double TitleSize { get; set; } = 17;

    /// <summary>
    /// حجم النصّ الثانويّ (التلميحات والشارات والمسارات). <b>0 = تلقائيّ</b>: يُشتقّ من
    /// <see cref="UiSize"/> ناقص نقطتين فيبقى التسلسل الهرميّ محفوظاً مع منزلق واحد. أيّ قيمة أكبر
    /// من الصفر تفكّ الاشتقاق وتُثبّت الحجم كما ضبطه المستخدم (من لا يقرأ النصّ الصغير يرفعه وحده).
    /// الصفر هو الافتراضيّ فتبقى ملفّات config.json القديمة على سلوكها السابق حرفيّاً.
    /// </summary>
    public double SmallSize { get; set; } = 0;

    /// <summary>عناوين فرعيّة داخل الأقسام. <b>0 = تلقائيّ</b> (نصّ الواجهة + 2).</summary>
    public double HeadingSize { get; set; }

    /// <summary>أدقّ نصّ في الواجهة: الشارات والعدّادات. <b>0 = تلقائيّ</b> (نصّ الواجهة − 4).</summary>
    public double TinySize { get; set; }

    /// <summary>النصوص التقنيّة أحاديّة العرض داخل الواجهة (المسارات وكتل الكود). <b>0 = تلقائيّ</b>.</summary>
    public double CodeSize { get; set; }

    /// <summary>أرقام وأيقونات العرض الكبيرة في حالات الفراغ وشاشة البدء. <b>0 = تلقائيّ</b>.</summary>
    public double DisplaySize { get; set; }

    /// <summary>استدارة حواف الكروت ونوافذ المشروع (نصف قطر الزاوية بالبكسل). 0 = زوايا حادّة.</summary>
    public double CornerRadius { get; set; } = 14;

    public FontSettings Clone() => (FontSettings)MemberwiseClone();
}

/// <summary>
/// يدير إعدادات التطبيق المحلّيّة: يحمّل/يحفظ <c>config.json</c>، ويطبّق القيم على موارد التطبيق
/// (<c>Font.Ui</c>/<c>Font.Mono</c> و<c>Size.*</c> و<c>Radius.*</c>) عبر <c>DynamicResource</c>
/// فتتحدّث الواجهة حيّاً. النوافذ/التيرمنال تشترك في <see cref="Changed"/> لتحدّث ما لا يُشتَقّ من
/// الموارد (خطّ التيرمنال).
/// </summary>
public static class FontManager
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher");

    /// <summary>مسار ملفّ إعدادات التطبيق المحلّيّة (لفتحه من محرّر الإعدادات المدمج).</summary>
    public static string ConfigPath { get; } = Path.Combine(Dir, "config.json");

    /// <summary>المسار القديم (<c>fonts.json</c>) — يُهاجَر لمرّة واحدة إلى <see cref="ConfigPath"/>.</summary>
    private static readonly string LegacyPath = Path.Combine(Dir, "fonts.json");

    // ReadCommentHandling/AllowTrailingCommas: تسامح مع تعليقات أو فاصلة ذيليّة قد يضيفها المستخدم في
    // المحرّر، فلا تنهار القراءة وتُفقَد الإعدادات صامتةً.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static FontSettings Current { get; private set; } = new();

    /// <summary>يُطلَق بعد كلّ <see cref="Apply"/> — تشترك فيه النوافذ/التيرمنال لتحدّث خطوطها.</summary>
    public static event Action? Changed;

    // الافتراضيّات الأصليّة تُلتقَط من الموارد أوّل مرّة كي نستعيدها عند إفراغ الحقل (بلا إعادة بناء pack://).
    private static FontFamily? _defaultUi;
    private static FontFamily? _defaultMono;

    /// <summary>يقرأ الإعدادات من القرص (أو الافتراضيّ إن غاب الملف/تلِف)، مع هجرة fonts.json القديم.</summary>
    public static void Load()
    {
        try
        {
            string? path = File.Exists(ConfigPath) ? ConfigPath
                         : MigrateLegacy();
            if (path != null && File.Exists(path))
                Current = JsonSerializer.Deserialize<FontSettings>(File.ReadAllText(path), Options) ?? new();
        }
        catch { Current = new(); }
    }

    /// <summary>
    /// هجرة لمرّة واحدة: إن وُجد <c>fonts.json</c> القديم يُنسَخ محتواه إلى <c>config.json</c> ويُعاد
    /// تسمية القديم <c>.migrated</c> كي لا يتكرّر. تُعيد مسار الملفّ المقروء (config.json الجديد) أو null.
    /// </summary>
    private static string? MigrateLegacy()
    {
        try
        {
            if (!File.Exists(LegacyPath)) return null;
            Directory.CreateDirectory(Dir);
            File.Copy(LegacyPath, ConfigPath, overwrite: true);
            File.Move(LegacyPath, LegacyPath + ".migrated", overwrite: true);
            return ConfigPath;
        }
        catch { return File.Exists(LegacyPath) ? LegacyPath : null; }
    }

    /// <summary>يحفظ الإعدادات الحاليّة إلى <c>config.json</c>.</summary>
    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current, Options));
        }
        catch { /* غير حرِج */ }
    }

    /// <summary>يستبدل الإعدادات ويحفظ ويطبّق (من لوحة الضبط).</summary>
    public static void Update(FontSettings s) { Current = s; Save(); Apply(); }

    /// <summary>يعيد قراءة الملف من القرص ثمّ يطبّقه — بعد تعديل الملف يدويّاً/من المحرّر.</summary>
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

        double menu = Clamp(s.MenuSize);
        res["Size.Ui"]    = Clamp(s.UiSize);
        res["Size.Menu"]  = menu;
        res["Size.Table"] = Clamp(s.TableSize);
        // النصّ الثانويّ (العناوين الفرعيّة/الشارات): يتبع حجم نصّ الواجهة ناقصاً قليلاً ما دام على
        // التلقائيّ (0)، فتتناسق القوائم وتكبر بالكامل من منزلق واحد مع الحفاظ على التسلسل الهرميّ؛
        // وإن ضبطه المستخدم صراحةً احتُرمت قيمته.
        res["Size.Small"] = Clamp(s.SmallSize > 0 ? s.SmallSize : s.UiSize - 2);
        res["Size.Title"] = Clamp(s.TitleSize);
        // بقيّة الأدوار تُشتقّ من نصّ الواجهة ما لم يضبطها المستخدم: منزلقٌ واحد يكبّر التطبيق كلّه
        // متناسقاً، ومن أراد التفصيل فكّ الاشتقاق بضبط الدور الذي يريده وحده.
        res["Size.Heading"] = Clamp(s.HeadingSize > 0 ? s.HeadingSize : s.UiSize + 2);
        res["Size.Tiny"]    = Clamp(s.TinySize    > 0 ? s.TinySize    : s.UiSize - 4);
        res["Size.Code"]    = Clamp(s.CodeSize    > 0 ? s.CodeSize    : s.UiSize - 1);
        res["Size.Display"] = Clamp(s.DisplaySize > 0 ? s.DisplaySize : s.UiSize * 2.2, 7, 96);
        // أيقونات القائمة تتبع حجم المنيو (مطابقة للعنوان)، ونصّ الاختصار أصغر بقدر بسيط.
        res["Size.MenuIcon"]    = menu;
        res["Size.MenuGesture"] = Math.Max(9, menu - 2);

        // استدارة الحواف: مورد CornerRadius مشترك للكروت ونوافذ المشروع، ونصفه للعناصر الصغيرة.
        double r = ClampRadius(s.CornerRadius);
        res["Radius.Card"]    = new CornerRadius(r);
        res["Radius.Control"] = new CornerRadius(Math.Min(r, Math.Max(6, r * 0.72)));
        // حبّة البحث: استدارة كاملة تقريباً مهما كان إعداد الاستدارة (نصف ارتفاع الحقل = 15).
        res["Radius.Pill"]    = new CornerRadius(Math.Max(r, 15));

        Changed?.Invoke();
    }

    /// <summary>خطّ من اسم المستخدم (مع احتياطات) أو الافتراضيّ الأصليّ إن كان فارغاً.</summary>
    private static FontFamily FamilyOr(string name, FontFamily? fallback)
        => string.IsNullOrWhiteSpace(name)
            ? fallback ?? new FontFamily("Segoe UI")
            : new FontFamily(name.Trim() + ", Segoe UI, Tahoma");

    private static double Clamp(double v) => Math.Clamp(v, 7, 40);

    /// <summary>حدود مخصّصة — أحجام العرض الكبيرة تتجاوز سقف الأربعين.</summary>
    private static double Clamp(double v, double min, double max) => Math.Clamp(v, min, max);
    private static double ClampRadius(double v) => Math.Clamp(v, 0, 28);
}
