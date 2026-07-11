using System.Collections.Generic;

namespace TerminalLauncher.Theme;

public enum ThemeMode { Dark, Light }

/// <summary>تفضيلات المظهر المحفوظة (تبقى بين التشغيلات).</summary>
public sealed class AppSettings
{
    /// <summary>الوضع (داكن/فاتح). يُشتقّ من الثيم المختار عند التطبيق؛ يبقى للتبديل السريع.</summary>
    public ThemeMode Mode { get; set; } = ThemeMode.Dark;

    /// <summary>معرّف الثيم المُسمّى المختار (مثل "helium-dark"). راجع <see cref="ThemeManager.Presets"/>.</summary>
    public string ThemePresetId { get; set; } = "helium-dark";

    /// <summary>مزامنة الفاتح/الداكن مع وضع نظام ويندوز (يتجاوز الثيم المختار عند التفعيل).</summary>
    public bool SyncThemeWithOs { get; set; } = false;

    /// <summary>لغة الواجهة: "ar" (عربي، RTL) أو "en" (إنجليزي، LTR).</summary>
    public string Language { get; set; } = "ar";

    /// <summary>مهجور — بقي لتوافق قراءة الإعدادات القديمة. لون التمييز صار جزءاً من الثيم.</summary>
    public int AccentIndex { get; set; } = 0;
    public double BackgroundOpacity { get; set; } = 0.82;

    /// <summary>مسار صورة خلفيّة التطبيق (اختياريّ). عند ضبطه تُرسَم خلفيّة النافذة صورةً
    /// ويُصبح التيرمنال شبه شفّاف (بمقدار <see cref="BackgroundOpacity"/>) فتظهر الصورة خلفه.</summary>
    public string BackgroundImagePath { get; set; } = "";

    /// <summary>
    /// صور الخلفيّة التي رفعها المستخدم (مسارات مطلقة). تظهر كمصغّرات قابلة لإعادة الاختيار في معرض
    /// الخلفيّة، فلا تُفقَد الصورة المرفوعة عند تبديل الخلفيّة. أحدث المرفوعات أوّلاً.
    /// </summary>
    public List<string> CustomBackgroundImages { get; set; } = new();

    /// <summary>
    /// نوع الخلفيّة المختارة: "theme" (خلفيّة الثيم، الافتراضي)، "solid" (لون مصمت)،
    /// "gradient" (تدرّج)، "pattern" (نقش)، "image" (صورة مخصّصة عبر <see cref="BackgroundImagePath"/>).
    /// أيّ نوع ≠ "theme" يجعل التيرمنال شبه شفّاف ليظهر خلفه.
    /// </summary>
    public string BackgroundKind { get; set; } = "theme";

    /// <summary>
    /// قيمة الخلفيّة الموافقة لـ <see cref="BackgroundKind"/>: hex للّون المصمت (#RRGGBB)،
    /// أو معرّف قالب للتدرّج/النقش. غير مستعملة للصورة (تُقرأ من <see cref="BackgroundImagePath"/>) ولا للثيم.
    /// </summary>
    public string BackgroundValue { get; set; } = "";

    /// <summary>حجم خطّ التيرمنال (يُغيَّر بـ Ctrl +/-؛ يبقى بين التشغيلات).</summary>
    public double TerminalFontSize { get; set; } = 13;

    /// <summary>نوع خطّ التيرمنال (أحاديّ المسافة). اسم واحد؛ يُضاف Consolas احتياطياً عند التطبيق.</summary>
    public string FontFamily { get; set; } = "Cascadia Mono";

    /// <summary>لون الكتابة الافتراضي (النصّ بلا SGR) بصيغة #RRGGBB.</summary>
    public string DefaultForeground { get; set; } = "#D4D4D4";

    /// <summary>
    /// تفعيل مساعد الـ AI السياقيّ (اختياريّ — مُعطَّل افتراضاً). عند التعطيل تُظهِر أفعال «اشرح هذه الكتلة»
    /// تلميحاً بكيفيّة التفعيل ولا تستدعي أيّ خدمة خارجيّة.
    /// </summary>
    public bool AiAssistantEnabled { get; set; } = false;

    /// <summary>
    /// آخر نسخة عُرِضت لها لوحة «ما الجديد» تلقائياً. تقارنها <c>WhatsNewWindow.ShowIfNew</c> بـ
    /// <c>AppVersion.Current</c>: إن اختلفتا تُعرَض اللوحة مرّة واحدة ثم تُخزَّن النسخة الحالية هنا،
    /// فلا تتكرّر لكلّ إصدار. فارغة = لم تُعرَض بعد (أوّل تشغيل).
    /// </summary>
    public string LastWhatsNewVersion { get; set; } = "";
}
