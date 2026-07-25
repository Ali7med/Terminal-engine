using System;
using System.IO;
using TerminalLauncher.Theme;

namespace TerminalLauncher.Services;

/// <summary>
/// تلميحات إقلاع خفيفة (معرّف الثيم + لغة الواجهة) في ملفّ نصّيّ صغير بجوار قاعدة البيانات.
///
/// <para><b>سببها الوحيد شاشة البدء:</b> تُعرض قبل أن يُفتح SQLite بأجزاء من الثانية، فلو قرأت
/// تفضيلات المظهر من <see cref="SettingsStore"/> لأخّرت أوّل بكسل بقدر تهيئة قاعدة البيانات كاملةً —
/// وهي أثقل ما في الإقلاع. هذا الملفّ سطران بصيغة <c>key=value</c> يُقرآن ويُحلَّلان بلا مُسلسِل
/// ولا قاعدة بيانات.</para>
///
/// المصدر الأصل يبقى <see cref="SettingsStore"/>؛ هذا نسخة مشتقّة تُكتب مع كلّ حفظ للإعدادات.
/// إن فُقد الملفّ أو تلف (أوّل تشغيل، أو حذف مجلّد البيانات) نعود إلى وضع ويندوز (فاتح/داكن)
/// والعربيّة — فلا يفشل الإقلاع بسبب تلميح.
/// </summary>
public static class BootProfile
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher", "boot.txt");

    /// <summary>تلميحات الإقلاع: معرّف الثيم المطبَّق ورمز اللغة ("ar"/"en").</summary>
    public readonly record struct Hints(string ThemeId, string Lang)
    {
        public bool IsArabic => !string.Equals(Lang, "en", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>يقرأ وضع ويندوز (فاتح/داكن) من الريجستري؛ الافتراضي داكن عند التعذّر.</summary>
    public static bool IsOsLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// التلميحات المحفوظة، أو المشتقّة من وضع ويندوز إن لم توجد. لا ترمي أبداً.
    /// مع تفعيل «مزامنة الثيم مع النظام» يُشتقّ الثيم من الوضع الحاليّ لا من القيمة المحفوظة،
    /// وإلّا تأخّرت الشاشة عن النظام إقلاعاً كاملاً بعد تبديل وضع ويندوز.
    /// </summary>
    public static Hints Load()
    {
        string themeId = ThemeManager.DefaultFor(IsOsLightTheme() ? ThemeMode.Light : ThemeMode.Dark);
        string lang = "ar";

        try
        {
            if (File.Exists(FilePath))
            {
                bool syncOs = false;
                string? saved = null;

                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "theme": saved = value; break;
                        case "lang":  lang = value; break;
                        case "syncOs": syncOs = value == "1"; break;
                    }
                }

                if (!syncOs && !string.IsNullOrEmpty(saved)) themeId = saved!;
            }
        }
        catch { /* تلميح تالف = تلميح غائب */ }

        return new Hints(themeId, lang);
    }

    /// <summary>يكتب التلميحات من الإعدادات المحفوظة. لا ترمي أبداً (الحفظ الحقيقيّ تمّ قبلها).</summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(FilePath,
                "theme=" + settings.ThemePresetId + Environment.NewLine +
                "lang=" + (string.IsNullOrWhiteSpace(settings.Language) ? "ar" : settings.Language) + Environment.NewLine +
                "syncOs=" + (settings.SyncThemeWithOs ? "1" : "0") + Environment.NewLine);
        }
        catch { /* تلميح تعذّرت كتابته يعني شاشة بدء بألوان النظام — لا أكثر */ }
    }
}
