namespace TerminalLauncher.Services;

/// <summary>
/// المصدر الوحيد لحقيقة إصدار الأداة (Single Source of Truth).
/// كل كود يحتاج رقم الإصدار يقرأه من هنا: <c>AppVersion.Current</c> — لا أرقام إصدار مبعثرة في XAML/code.
///
/// نظام الترقيم: MAJOR.MINOR.PATCH (Semantic-lite)
///   • MAJOR — تغيير جذري كبير في البنية أو التوافق.
///   • MINOR — «موجة تغيير مكتملة» (الافتراضي). كل موجة عمل مكتملة ترفع MINOR وتصفّر PATCH.
///   • PATCH — إصلاح/تعديل صغير ضمن الموجة نفسها.
///
/// القاعدة (الدستور): مع كل موجة تغيير مكتملة يجب:
///   1) رفع <see cref="Current"/> (MINOR افتراضاً + تصفير PATCH) وتحديث <see cref="ReleasedDate"/>.
///   2) إضافة مدخلة مطابقة في CHANGELOG.md (HIGHLIGHTS / NEW / IMPROVED / FIXED).
/// ولوحة «ما الجديد» (<c>WhatsNewWindow</c>) تقرأ من CHANGELOG.md مباشرةً وقت التشغيل.
/// </summary>
public static class AppVersion
{
    /// <summary>رقم الإصدار الحالي (MAJOR.MINOR.PATCH). يستهلكه شريط العنوان ولوحة «ما الجديد» وبطاقة «حول».</summary>
    public const string Current = "1.54.0";

    /// <summary>تاريخ إصدار النسخة الحالية (ISO: yyyy-MM-dd) — يُعرض ضمن ترويسة النسخة في لوحة «ما الجديد».</summary>
    public const string ReleasedDate = "2026-07-24";

    /// <summary>نسخة معروضة رباعية للتوافق مع صيغة «x.x.x.x» عند الحاجة (نُلحق 0 كبناء).</summary>
    public static string Display4 => Current + ".0";
}
