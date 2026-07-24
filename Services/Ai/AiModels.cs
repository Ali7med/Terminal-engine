using System;
using System.Collections.Generic;

namespace TerminalLauncher.Services.Ai;

/// <summary>دور الرسالة في المحادثة.</summary>
public enum AiRole
{
    /// <summary>تعليمات النظام (البادئة الثابتة — تُوضَع أوّلاً كي يستفيد التخزين المؤقّت للبرومبت).</summary>
    System,

    /// <summary>رسالة المستخدم.</summary>
    User,

    /// <summary>ردّ المساعد.</summary>
    Assistant,
}

/// <summary>رسالة واحدة في المحادثة.</summary>
/// <param name="Role">دور صاحب الرسالة.</param>
/// <param name="Content">نصّ الرسالة.</param>
public sealed record AiMessage(AiRole Role, string Content)
{
    /// <summary>يبني رسالة نظام.</summary>
    public static AiMessage System(string content) => new(AiRole.System, content);

    /// <summary>يبني رسالة مستخدم.</summary>
    public static AiMessage User(string content) => new(AiRole.User, content);

    /// <summary>يبني ردّ مساعد.</summary>
    public static AiMessage Assistant(string content) => new(AiRole.Assistant, content);
}

/// <summary>مقطع واحد من البثّ: إمّا نصّ، وإمّا إحصاء استهلاك يصل في نهايته.</summary>
public readonly struct AiDelta
{
    /// <summary>النصّ المُضاف في هذا المقطع (قد يكون فارغاً في مقطع الاستهلاك).</summary>
    public string Text { get; }

    /// <summary>عدد توكنز الإدخال إن أبلغ عنه المزوّد (وإلّا null).</summary>
    public int? PromptTokens { get; }

    /// <summary>عدد توكنز الإخراج إن أبلغ عنه المزوّد (وإلّا null).</summary>
    public int? CompletionTokens { get; }

    private AiDelta(string text, int? promptTokens, int? completionTokens)
    {
        Text = text;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
    }

    /// <summary>مقطع نصّي.</summary>
    public static AiDelta OfText(string text) => new(text, null, null);

    /// <summary>مقطع استهلاك (بلا نصّ) — لا تُبلغ عنه كلّ المنصّات.</summary>
    public static AiDelta OfUsage(int? prompt, int? completion) => new(string.Empty, prompt, completion);

    /// <summary>هل هذا مقطع استهلاك لا نصّ فيه؟</summary>
    public bool IsUsage => Text.Length == 0 && (PromptTokens.HasValue || CompletionTokens.HasValue);
}

/// <summary>
/// تصنيف موحّد لأخطاء المزوّدين. كلّ منصّة ترجع أجسام أخطاء بشكل مختلف؛ تُطبَّع كلّها إلى هذا
/// التصنيف كي تعرض الواجهة رسالة مفهومة وزرّ إجراء مناسباً بدل نصّ خطأ خام.
/// </summary>
public enum AiErrorKind
{
    /// <summary>تعذّر الوصول للخدمة (DNS/اتّصال/انقطاع أثناء البثّ).</summary>
    Network,

    /// <summary>انتهت المهلة (اتّصال أوّليّ أو خمول بين المقاطع).</summary>
    Timeout,

    /// <summary>مفتاح غير صالح أو مفقود أو منتهي (401/403).</summary>
    Auth,

    /// <summary>تجاوز حدّ المعدّل — مؤقّت، يُعاد بعد <see cref="AiException.RetryAfter"/> (429).</summary>
    RateLimit,

    /// <summary>نفاد الرصيد/الحصّة — لا ينفع معه الانتظار (يحتاج فوترة).</summary>
    Quota,

    /// <summary>السياق أطول ممّا يقبله النموذج.</summary>
    ContextOverflow,

    /// <summary>النموذج المطلوب غير متاح لهذا الحساب/المزوّد.</summary>
    ModelUnavailable,

    /// <summary>خطأ من طرف المزوّد (5xx) أو ردّ غير مفهوم.</summary>
    Provider,

    /// <summary>ألغى المستخدم الطلب.</summary>
    Canceled,
}

/// <summary>
/// خطأ مزوّد مُطبَّع. يحمل ما تحتاجه الواجهة لبناء رسالة بشريّة: التصنيف، واسم المزوّد والنموذج،
/// ومدّة الانتظار عند حدّ المعدّل. <b>لا يحمل المفتاح ولا أيّ سرّ</b> — ولا يُسجَّل في أيّ سجلّ.
/// </summary>
public sealed class AiException : Exception
{
    /// <summary>تصنيف الخطأ.</summary>
    public AiErrorKind Kind { get; }

    /// <summary>الاسم المعروض للمزوّد (مثل "Kimi (Moonshot)").</summary>
    public string ProviderName { get; }

    /// <summary>معرّف النموذج المستعمَل وقت الخطأ.</summary>
    public string Model { get; }

    /// <summary>مدّة الانتظار المقترحة عند <see cref="AiErrorKind.RateLimit"/> (من ترويسة Retry-After).</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>نصّ المزوّد الأصليّ (مقتطع) — للتشخيص فقط، لا يُعرض بمفرده للمستخدم.</summary>
    public string? RawDetail { get; }

    public AiException(
        AiErrorKind kind,
        string providerName,
        string model,
        string message,
        TimeSpan? retryAfter = null,
        string? rawDetail = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        ProviderName = providerName;
        Model = model;
        RetryAfter = retryAfter;
        RawDetail = rawDetail;
    }
}

/// <summary>خيارات نداء واحد.</summary>
public sealed class AiChatOptions
{
    /// <summary>معرّف النموذج (مثل "kimi-k2-0905-preview"). إلزاميّ.</summary>
    public string Model { get; init; } = "";

    /// <summary>حدّ توكنز الردّ. null = ترك القرار للمزوّد.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>درجة العشوائيّة. null = افتراضيّ المزوّد.</summary>
    public double? Temperature { get; init; }
}

/// <summary>نتيجة «اختبار الاتّصال»: نجاح مع تفصيل، أو فشل مُصنَّف.</summary>
/// <param name="Ok">هل نجح الاتّصال؟</param>
/// <param name="Kind">تصنيف الفشل (null عند النجاح).</param>
/// <param name="Detail">تفصيل قصير يُعرض للمستخدم (عدد النماذج المتاحة، أو سبب الفشل).</param>
public sealed record AiProbeResult(bool Ok, AiErrorKind? Kind, string Detail)
{
    /// <summary>نجاح باختصار.</summary>
    public static AiProbeResult Success(string detail) => new(true, null, detail);

    /// <summary>فشل مُصنَّف.</summary>
    public static AiProbeResult Failure(AiErrorKind kind, string detail) => new(false, kind, detail);
}

/// <summary>
/// أعلام قدرات المزوّد. «التوافق مع OpenAI» طيف لا قيمة منطقيّة واحدة: المنصّات تختلف في أسلوب
/// المصادقة، وفي الإبلاغ عن الاستهلاك أثناء البثّ، وفي دعم دور النظام. هذه الأعلام تحمل تلك الفروق
/// بدل تفريعها داخل الكود.
/// </summary>
public sealed class AiCapabilities
{
    /// <summary>المفتاح اختياريّ (Ollama المحلّيّ لا يحتاج مفتاحاً).</summary>
    public bool KeyOptional { get; init; }

    /// <summary>يدعم <c>stream_options.include_usage</c> لإرجاع الاستهلاك ضمن البثّ.</summary>
    public bool UsageInStream { get; init; }

    /// <summary>يدعم دور «system» (وإلّا يُدمَج نصّ النظام في أوّل رسالة مستخدم).</summary>
    public bool SupportsSystemRole { get; init; } = true;

    /// <summary>حدّ نافذة السياق المعروف تقريباً (توكنز) — للتحذير قبل الإرسال. 0 = غير معروف.</summary>
    public int ContextWindow { get; init; }
}
