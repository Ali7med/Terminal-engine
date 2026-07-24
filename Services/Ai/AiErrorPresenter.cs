using System;
using System.Globalization;

namespace TerminalLauncher.Services.Ai;

/// <summary>الإجراء المقترح بجانب رسالة الخطأ — «ماذا أفعل الآن؟».</summary>
public enum AiErrorAction
{
    /// <summary>لا إجراء (إلغاء المستخدم مثلاً).</summary>
    None,

    /// <summary>افتح إعدادات الـAI (مفتاح خاطئ/نموذج غير متاح).</summary>
    OpenSettings,

    /// <summary>أعد المحاولة (شبكة/مهلة/خطأ مزوّد عابر).</summary>
    Retry,

    /// <summary>افتح صفحة فوترة المزوّد (نفاد الرصيد).</summary>
    OpenBilling,

    /// <summary>قلّص السياق المُرسَل (تجاوز نافذة النموذج).</summary>
    TrimContext,
}

/// <summary>رسالة خطأ جاهزة للعرض: نصّ بشريّ + إجراء + وسم المزوّد والنموذج.</summary>
/// <param name="Message">الرسالة المعروضة.</param>
/// <param name="Action">الإجراء المقترح.</param>
/// <param name="ActionLabel">تسمية زرّ الإجراء (فارغة إن لا إجراء).</param>
/// <param name="Origin">«المزوّد · النموذج» — يظهر تحت الرسالة.</param>
/// <param name="RetryAfter">مدّة الانتظار المقترحة عند حدّ المعدّل.</param>
public sealed record AiErrorView(
    string Message,
    AiErrorAction Action,
    string ActionLabel,
    string Origin,
    TimeSpan? RetryAfter);

/// <summary>
/// يحوّل <see cref="AiException"/> المُطبَّع إلى رسالة يفهمها المستخدم وزرّ يفعل شيئاً مفيداً.
/// <para>هذه الطبقة هي ما يبرّر تطبيع الأخطاء أصلاً: «مفتاح خاطئ» و«تجاوزت حدّ المعدّل» و«نفد
/// رصيدك» و«انقطعت الشبكة» أربع رسائل بأربعة إجراءات مختلفة — وعرضها كلّها كنصّ خطأ خام واحد
/// يترك المستخدم بلا خطوة تالية.</para>
/// </summary>
public static class AiErrorPresenter
{
    /// <summary>يبني عرض الخطأ. لا يكشف المفتاح ولا يُظهر نصّ المزوّد الخام.</summary>
    public static AiErrorView Present(AiException error)
    {
        if (error is null) throw new ArgumentNullException(nameof(error));

        string origin = string.IsNullOrEmpty(error.Model)
            ? error.ProviderName
            : $"{error.ProviderName} · {error.Model}";

        (string message, AiErrorAction action) = error.Kind switch
        {
            AiErrorKind.Auth => (Loc.T("ai.err.auth"), AiErrorAction.OpenSettings),
            AiErrorKind.Quota => (Loc.T("ai.err.quota"), AiErrorAction.OpenBilling),
            AiErrorKind.RateLimit => (RateLimitMessage(error.RetryAfter), AiErrorAction.Retry),
            AiErrorKind.ContextOverflow => (Loc.T("ai.err.context"), AiErrorAction.TrimContext),
            AiErrorKind.ModelUnavailable => (Loc.T("ai.err.model"), AiErrorAction.OpenSettings),
            AiErrorKind.Network => (Loc.T("ai.err.network"), AiErrorAction.Retry),
            AiErrorKind.Timeout => (Loc.T("ai.err.timeout"), AiErrorAction.Retry),
            AiErrorKind.Canceled => (Loc.T("ai.err.canceled"), AiErrorAction.None),
            _ => (Loc.T("ai.err.provider"), AiErrorAction.Retry),
        };

        return new AiErrorView(message, action, LabelOf(action), origin, error.RetryAfter);
    }

    private static string RateLimitMessage(TimeSpan? retryAfter)
    {
        if (retryAfter is not TimeSpan wait || wait <= TimeSpan.Zero)
            return Loc.T("ai.err.rate");

        string seconds = Math.Ceiling(wait.TotalSeconds).ToString("0", CultureInfo.InvariantCulture);
        return string.Format(CultureInfo.InvariantCulture, Loc.T("ai.err.rateWait"), seconds);
    }

    private static string LabelOf(AiErrorAction action) => action switch
    {
        AiErrorAction.OpenSettings => Loc.T("ai.act.settings"),
        AiErrorAction.Retry => Loc.T("ai.act.retry"),
        AiErrorAction.OpenBilling => Loc.T("ai.act.billing"),
        AiErrorAction.TrimContext => Loc.T("ai.act.trim"),
        _ => "",
    };
}
