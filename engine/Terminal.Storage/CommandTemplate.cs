using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Terminal.Storage;

/// <summary>
/// قالب أمر مُطبَّع: نصّ الأمر بعد استبدال الأجزاء المتغيّرة (المسارات، الأرقام، النصوص المقتبسة،
/// المعرّفات) بعلامات ثابتة، مع بصمة قصيرة له.
/// </summary>
/// <param name="Template">النصّ المُطبَّع (مثل <c>git checkout &lt;str&gt;</c>).</param>
/// <param name="Hash">بصمة القالب (16 خانة hex) — مفتاح التجميع.</param>
public readonly record struct CommandTemplateInfo(string Template, string Hash);

/// <summary>
/// تطبيع الأوامر إلى قوالب. هذا حجر الأساس لمبدأ <b>«تجميع لا أرشفة»</b>: بدل تخزين كلّ تنفيذ
/// كسطر مستقلّ (نموّ غير محدود + أسرار مكتوبة داخل الأوامر على القرص)، نخزّن قالباً واحداً
/// بعدّادات. <c>git push origin feature/x</c> و<c>git push origin fix/y</c> يصيران قالباً واحداً.
///
/// <para>يُستعمل نفس المُطبِّع في طرفَي المقارنة عند جسر «كتالوج الأوامر»: نُطبّع مدخلات الكتالوج
/// أيضاً ونقارن البصمات — وإلّا اقترحنا على المستخدم حفظ أمر يملكه أصلاً.</para>
/// </summary>
public static class CommandTemplate
{
    // الترتيب مقصود: الأطول/الأكثر تحديداً أوّلاً كي لا يبتلع نمطٌ عامّ نمطاً خاصّاً.
    private static readonly (Regex Pattern, string Token)[] Rules =
    {
        // عناوين URL كاملة.
        (new Regex(@"\b[a-z][a-z0-9+.-]*://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled), "<url>"),

        // مسارات ويندوز المطلقة (C:\…) و UNC (\\server\share).
        (new Regex(@"(?:[a-zA-Z]:\\|\\\\)[^\s""']*", RegexOptions.Compiled), "<path>"),

        // مسارات يونكس المطلقة و~/…
        (new Regex(@"(?<![\w.])(?:~|\.{1,2})?/[^\s""']+", RegexOptions.Compiled), "<path>"),

        // مسارات وأسماء نسبيّة فيها فاصل: أسماء الفروع (feature/login)، والمسارات النسبيّة
        // (src/app.ts)، والمراجع البعيدة (origin/main). هذه أكثر الأجزاء المتغيّرة شيوعاً في أوامر
        // المطوّرين — بلا تطبيعها يصير لكلّ فرع صفٌّ مستقلّ ويسقط مبدأ «تجميع لا أرشفة».
        (new Regex(@"(?<![\w.@/\\<-])[\w.@-]+(?:[/\\][\w.@-]+)+", RegexOptions.Compiled), "<path>"),

        // نصوص مقتبسة (مزدوجة ثمّ مفردة).
        (new Regex("\"[^\"]*\"", RegexOptions.Compiled), "<str>"),
        (new Regex(@"'[^']*'", RegexOptions.Compiled), "<str>"),

        // GUID.
        (new Regex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "<id>"),

        // بصمات/هاشات طويلة (sha، معرّفات صور، …).
        (new Regex(@"\b[0-9a-f]{12,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "<id>"),

        // أرقام إصدار/منافذ/أعداد.
        (new Regex(@"(?<![\w<])\d+(?:\.\d+)*(?![\w>])", RegexOptions.Compiled), "<n>"),
    };

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>يُطبّع أمراً ويعيد قالبه وبصمته. أمر فارغ يعيد قالباً فارغاً ببصمة ثابتة.</summary>
    public static CommandTemplateInfo Normalize(string? command)
    {
        string text = (command ?? string.Empty).Trim();
        if (text.Length == 0)
            return new CommandTemplateInfo(string.Empty, Fingerprint(string.Empty));

        foreach ((Regex pattern, string token) in Rules)
            text = pattern.Replace(text, token);

        text = Whitespace.Replace(text, " ").Trim();
        return new CommandTemplateInfo(text, Fingerprint(text));
    }

    /// <summary>
    /// بصمة بصمة نصّ (16 خانة hex من SHA-256). قصيرة كفاية لتكون مفتاحاً مقروءاً، وطويلة كفاية
    /// كي لا تتصادم عملياً على أحجام البيانات هنا.
    /// </summary>
    public static string Fingerprint(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        var sb = new StringBuilder(16);
        for (int i = 0; i < 8; i++)
            sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>
    /// بصمة خطأ = رمز الخروج + أوّل سطر خطأ مُطبَّعاً (بلا مسارات ولا أرقام). تجعل نفس الخطأ
    /// في مشروعين مختلفين يُطابق نفسه، فلا تتكرّر رقاقة «اشرح هذا الخطأ؟» لكلّ حالة.
    /// </summary>
    public static string ErrorFingerprint(int? exitCode, string? firstErrorLine)
    {
        string normalized = Normalize(firstErrorLine).Template;
        return Fingerprint($"{exitCode?.ToString(CultureInfo.InvariantCulture) ?? "?"}|{normalized}");
    }
}
