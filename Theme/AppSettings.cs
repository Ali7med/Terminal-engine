using System.Collections.Generic;

namespace TerminalLauncher.Theme;

public enum ThemeMode { Dark, Light }

/// <summary>تفضيلات المظهر المحفوظة (تبقى بين التشغيلات).</summary>
public sealed class AppSettings
{
    /// <summary>الوضع (داكن/فاتح). يُشتقّ من الثيم المختار عند التطبيق؛ يبقى للتبديل السريع.</summary>
    public ThemeMode Mode { get; set; } = ThemeMode.Dark;

    /// <summary>معرّف الثيم المُسمّى المختار (مثل "helium-dark"). راجع <see cref="ThemeManager.Presets"/>.</summary>
    public string ThemePresetId { get; set; } = "cozy-dark";

    /// <summary>مزامنة الفاتح/الداكن مع وضع نظام ويندوز (يتجاوز الثيم المختار عند التفعيل).</summary>
    public bool SyncThemeWithOs { get; set; } = false;

    /// <summary>الشريط الجانبيّ موسَّع (لوحة مشاريع كاملة) أم مطويّ إلى شريط أيقونيّ. يبقى بين التشغيلات.</summary>
    public bool SidebarExpanded { get; set; } = true;

    /// <summary>
    /// صندوق التأليف المنفصل: يُكتب فيه الأمر ثمّ يُرسَل عند Enter. مُطفأ افتراضاً — النمط الافتراضيّ
    /// هو الإدخال inline على بطاقة الكتلة النشطة (نمط Warp). تفعيله يُظهر صندوقاً منفصلاً أسفل التيرمنال.
    /// راجع docs/WarpInputBox_Design.md.
    /// </summary>
    public bool UseCommandComposer { get; set; } = true;

    /// <summary>مهجور — حارس ترقية قديم (كان يُطفئ الصندوق). يبقى لتوافق قراءة الإعدادات القديمة.</summary>
    public bool InlineInputMigrated { get; set; } = false;

    /// <summary>حارس ترقية «صندوق Warp الغنيّ»: يُعيد تفعيل الصندوق لمرّة واحدة (يتجاوز الإطفاء السابق).</summary>
    public bool RichComposerApplied { get; set; } = false;

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
    public string BackgroundKind { get; set; } = "gradient";

    /// <summary>
    /// قيمة الخلفيّة الموافقة لـ <see cref="BackgroundKind"/>: hex للّون المصمت (#RRGGBB)،
    /// أو معرّف قالب للتدرّج/النقش. غير مستعملة للصورة (تُقرأ من <see cref="BackgroundImagePath"/>) ولا للثيم.
    /// </summary>
    public string BackgroundValue { get; set; } = "depth-cozy";

    /// <summary>حجم خطّ التيرمنال (يُغيَّر بـ Ctrl +/-؛ يبقى بين التشغيلات).</summary>
    public double TerminalFontSize { get; set; } = 13;

    /// <summary>نوع خطّ التيرمنال (أحاديّ المسافة). اسم واحد؛ يُضاف Consolas احتياطياً عند التطبيق.</summary>
    public string FontFamily { get; set; } = "Cascadia Mono";

    /// <summary>
    /// لون الكتابة الافتراضي (النصّ بلا SGR): "auto" (الافتراضي) = يتبع الثيم (فاتح على الداكن، داكن على
    /// الفاتح)، أو لون صريح بصيغة #RRGGBB. راجع <c>ThemeManager.ResolveTerminalForeground</c>.
    /// </summary>
    public string DefaultForeground { get; set; } = "auto";

    /// <summary>
    /// تفعيل مساعد الـ AI السياقيّ (اختياريّ — مُعطَّل افتراضاً). عند التعطيل تُظهِر أفعال «اشرح هذه الكتلة»
    /// تلميحاً بكيفيّة التفعيل ولا تستدعي أيّ خدمة خارجيّة.
    /// </summary>
    public bool AiAssistantEnabled { get; set; } = false;

    /// <summary>
    /// تفضيلات طبقة الـ AI (المزوّد، النموذج، المفاتيح المُعمّاة بـDPAPI، وضوابط السياق والتعلّم).
    /// راجع <see cref="TerminalLauncher.Services.Ai.AiSettings"/>.
    /// </summary>
    public TerminalLauncher.Services.Ai.AiSettings Ai { get; set; } = new();

    /// <summary>
    /// آخر نسخة عُرِضت لها لوحة «ما الجديد» تلقائياً. تقارنها <c>WhatsNewWindow.ShowIfNew</c> بـ
    /// <c>AppVersion.Current</c>: إن اختلفتا تُعرَض اللوحة مرّة واحدة ثم تُخزَّن النسخة الحالية هنا،
    /// فلا تتكرّر لكلّ إصدار. فارغة = لم تُعرَض بعد (أوّل تشغيل).
    /// </summary>
    public string LastWhatsNewVersion { get; set; } = "";

    /// <summary>
    /// علم الترحيل لمرّة واحدة: تحويل الأوامر المحفوظة القديمة (CommandEntry الموسومة) إلى أوامر داخل
    /// المشاريع (نموذج «لوحة المشاريع»). يُضبط true بعد أوّل ترحيل ناجح فلا يتكرّر ولا يطمس تعديلات المستخدم.
    /// </summary>
    public bool ProjectsMigratedV1 { get; set; } = false;

    /// <summary>
    /// علم ترحيل V2 لمرّة واحدة: نقل لون كلّ مشروع (النموذج القديم) إلى «تاك» بنفس الاسم واللون، وإسناده
    /// للمشروع. يُضبط true بعد أوّل ترحيل فلا يتكرّر.
    /// </summary>
    public bool ProjectsTagMigratedV2 { get; set; } = false;
}
