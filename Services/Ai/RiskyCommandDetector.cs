using System;
using System.Text.RegularExpressions;

namespace TerminalLauncher.Services.Ai;

/// <summary>
/// يميّز الأوامر المقترَحة الخطرة قبل إدراجها في سطر الإدخال.
///
/// <para><b>لماذا:</b> خرج التيرمنال مدخل غير موثوق — سكربت أو خادم بعيد يستطيع طباعة تعليمات
/// للنموذج تقوده إلى اقتراح أمر مدمّر. الأداة لا تنفّذ شيئاً تلقائياً أبداً، لكنّ الإدراج يضع
/// النصّ حيث تكفي ضغطة Enter واحدة — فالتمييز البصريّ هو الفاصل الأخير قبل تلك الضغطة.</para>
///
/// <para>هذا كاشف تحذير لا حاجز: لا يمنع شيئاً ولا يدّعي الإحاطة، بل يرفع الانتباه في الحالات
/// المعروفة الأشهر.</para>
/// </summary>
public static class RiskyCommandDetector
{
    private static readonly Regex[] Patterns =
    {
        // حذف تعاودي قسريّ.
        new(@"\brm\s+(-[a-zA-Z]*[rf][a-zA-Z]*\s+)+", RegexOptions.Compiled),
        new(@"\bRemove-Item\b.*\b-Recurse\b.*\b-Force\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\brmdir\s+/s\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // تنزيل ثمّ تنفيذ مباشر.
        new(@"\b(curl|wget|iwr|Invoke-WebRequest)\b[^|]*\|\s*(sudo\s+)?(ba|z|k|)sh\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bInvoke-Expression\b|\biex\b\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // كتابة على أجهزة الكتلة أو تهيئتها.
        new(@"\bdd\s+.*\bof=/dev/", RegexOptions.Compiled),
        new(@"\b(mkfs|format)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // تغيير صلاحيات/ملكيّة شامل على الجذر.
        new(@"\bchmod\s+(-R\s+)?777\b", RegexOptions.Compiled),
        new(@"\bchown\s+-R\b[^\n]*\s/\s*$", RegexOptions.Compiled),

        // تاريخ Git مدمِّر أو دفع قسريّ.
        new(@"\bgit\s+reset\s+--hard\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bgit\s+push\b[^\n]*\s(--force|-f)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bgit\s+clean\b[^\n]*-[a-zA-Z]*f", RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // مسارات نظام ويندوز.
        new(@"[A-Za-z]:\\Windows\\System32", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\breg\s+delete\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // رفع صلاحيات مع أمر غير مقروء.
        new(@"\bsudo\s+rm\b", RegexOptions.Compiled),
    };

    /// <summary>هل يطابق الأمر نمطاً خطراً معروفاً؟</summary>
    public static bool IsRisky(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;

        foreach (Regex pattern in Patterns)
            if (pattern.IsMatch(command))
                return true;

        return false;
    }

    /// <summary>
    /// ينظّف أمراً مقترَحاً للإدراج: سطر واحد بلا محرف سطر جديد. وجود سطر جديد في اللصق يعني
    /// تنفيذاً فوريّاً — وهو ما لا يحدث في هذه الأداة أبداً.
    /// </summary>
    public static string SanitizeForInsert(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "";

        string text = command.Replace("\r", " ").Replace("\n", " ").Trim();
        return Regex.Replace(text, @"\s{2,}", " ");
    }
}
