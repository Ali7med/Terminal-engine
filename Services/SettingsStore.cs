using System;
using System.IO;
using System.Text.Json;
using Terminal.Storage;
using TerminalLauncher.Theme;

namespace TerminalLauncher.Services;

/// <summary>
/// حفظ/تحميل تفضيلات المظهر في SQLite عبر <see cref="SettingsSqliteStore"/> (T-108)، مع هجرة
/// تلقائية لمرّة واحدة من ملف <c>settings.json</c> القديم. الواجهة العامّة (Load/Save) لم تتغيّر
/// فتبقى مواضع الاستعمال (MainWindow) كما هي.
/// </summary>
public sealed class SettingsStore
{
    private const string SettingsKey = "app_settings";

    private static readonly string LegacyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher", "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly SettingsSqliteStore _store = new(new AppDatabase());

    public AppSettings Load()
    {
        try
        {
            string? json = _store.Get(SettingsKey) ?? MigrateLegacyJson();
            if (string.IsNullOrEmpty(json)) return new();
            return MigrateToCozyDefaults(JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new());
        }
        catch
        {
            return new();
        }
    }

    /// <summary>
    /// ترقية لمرّة واحدة إلى مظهر الجيل الثاني «المريح»: تنقل من كان لا يزال على الافتراضيّ القديم
    /// (ثيم <c>helium-dark</c> + خلفيّة الثيم) إلى <c>cozy-dark</c> + حقل العمق الدافئ. من غيّر ثيمه
    /// أو خلفيّته يبقى على اختياره — لا نلمس تفضيلاً صريحاً للمستخدم.
    /// </summary>
    private static AppSettings MigrateToCozyDefaults(AppSettings s)
    {
        if (s.ThemePresetId == "helium-dark" && s.BackgroundKind == "theme")
        {
            s.ThemePresetId  = "cozy-dark";
            s.BackgroundKind = "gradient";
            s.BackgroundValue = "depth-cozy";
        }

        // ترقية لمرّة واحدة: النمط الافتراضيّ صار الإدخال inline (نمط Warp)، فنُطفئ صندوق التأليف
        // القديم لمن أبقاه مُفعَّلاً قبل هذا التغيير. من يريده يُعيد تفعيله من الإعدادات.
        if (!s.InlineInputMigrated)
        {
            s.UseCommandComposer = false;
            s.InlineInputMigrated = true;
        }
        return s;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            _store.Set(SettingsKey, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // تجاهل أخطاء الحفظ الطارئة.
        }
    }

    /// <summary>
    /// النصّ الخام (JSON) لإعدادات النظام كما هو مخزَّن — لعرضه/تحريره في محرّر الإعدادات المدمج.
    /// إن لم يوجد بعد (أوّل تشغيل) يُعاد تسلسل الافتراضيّات كي يرى المستخدم بنية كاملة يعدّلها.
    /// </summary>
    public string GetAppSettingsJson()
    {
        try
        {
            string? json = _store.Get(SettingsKey);
            if (!string.IsNullOrEmpty(json)) return json;
        }
        catch { /* أعد الافتراضيّ أدناه */ }
        return JsonSerializer.Serialize(new AppSettings(), Options);
    }

    /// <summary>
    /// يكتب نصّ JSON خاماً لإعدادات النظام (من المحرّر المدمج). لا يُطبَّق حيّاً — يُقرأ عند الإقلاع
    /// التالي. المتصل مسؤول عن التحقّق من صحّة الـ JSON قبل النداء.
    /// </summary>
    public void SetAppSettingsJson(string json)
    {
        try { _store.Set(SettingsKey, json); } catch { /* تجاهل */ }
    }

    /// <summary>قراءة قيمة مفتاح مستقلّ (لتفضيلات لا تخصّ المظهر، مثل حجم خطّ تفاصيل مراقب الخوادم).</summary>
    public string? GetRaw(string key)
    {
        try { return _store.Get(key); } catch { return null; }
    }

    /// <summary>حفظ قيمة مفتاح مستقلّ.</summary>
    public void SetRaw(string key, string value)
    {
        try { _store.Set(key, value); } catch { /* تجاهل */ }
    }

    /// <summary>
    /// هجرة لمرّة واحدة: إن وُجد <c>settings.json</c> القديم يُنقَل محتواه إلى SQLite ويُعاد تسميته
    /// <c>.migrated</c> كي لا يتكرّر. تُعيد الـ JSON المنقول (أو null إن لا ملف قديم).
    /// </summary>
    private string? MigrateLegacyJson()
    {
        try
        {
            if (!File.Exists(LegacyFilePath)) return null;
            string json = File.ReadAllText(LegacyFilePath);
            _store.Set(SettingsKey, json);
            File.Move(LegacyFilePath, LegacyFilePath + ".migrated", overwrite: true);
            return json;
        }
        catch
        {
            return null;
        }
    }
}
