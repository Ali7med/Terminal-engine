using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TerminalLauncher.Services.Ai;

/// <summary>
/// تطبيع أجسام أخطاء المزوّدين إلى <see cref="AiException"/> مُصنَّف. مشترك بين المحوّلين لأنّ
/// المنصّات تتّفق تقريباً على رموز الحالة وتختلف في شكل الجسم.
/// <para>رمز الحالة وحده لا يكفي: 429 قد يعني «انتظر ثوانيَ» أو «نفد رصيدك» — وهما إجراءان
/// مختلفان تماماً عند المستخدم، فنفتّش النصّ للتمييز.</para>
/// </summary>
internal static class AiErrorMapper
{
    /// <summary>يحوّل ردّ فشل إلى استثناء مُصنَّف برسالة عربيّة جاهزة للعرض.</summary>
    public static AiException Classify(
        HttpResponseMessage response,
        string body,
        string providerName,
        string model)
    {
        string lower = body.ToLowerInvariant();
        bool billing = IsBilling(lower);

        AiErrorKind kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiErrorKind.Auth,
            HttpStatusCode.PaymentRequired => AiErrorKind.Quota,
            HttpStatusCode.TooManyRequests => billing ? AiErrorKind.Quota : AiErrorKind.RateLimit,
            HttpStatusCode.NotFound => AiErrorKind.ModelUnavailable,
            HttpStatusCode.BadRequest when IsContextOverflow(lower) => AiErrorKind.ContextOverflow,
            HttpStatusCode.BadRequest when lower.Contains("model", StringComparison.Ordinal) => AiErrorKind.ModelUnavailable,
            >= HttpStatusCode.InternalServerError => AiErrorKind.Provider,
            _ => billing ? AiErrorKind.Quota : AiErrorKind.Provider,
        };

        string message = kind switch
        {
            AiErrorKind.Auth => "المفتاح غير صالح أو منتهي.",
            AiErrorKind.Quota => "نفد رصيد الحساب أو حصّته.",
            AiErrorKind.RateLimit => "تجاوزت حدّ المعدّل المسموح.",
            AiErrorKind.ContextOverflow => "السياق أطول ممّا يقبله هذا النموذج.",
            AiErrorKind.ModelUnavailable => $"النموذج «{model}» غير متاح لدى هذا المزوّد.",
            _ => $"ردّ المزوّد بخطأ ({(int)response.StatusCode}).",
        };

        return new AiException(kind, providerName, model, message, ReadRetryAfter(response), ExtractProviderMessage(body));
    }

    private static bool IsBilling(string lower)
        => lower.Contains("insufficient", StringComparison.Ordinal)
           || lower.Contains("quota", StringComparison.Ordinal)
           || lower.Contains("credit", StringComparison.Ordinal)
           || lower.Contains("billing", StringComparison.Ordinal)
           || lower.Contains("exceeded your current", StringComparison.Ordinal);

    private static bool IsContextOverflow(string lower)
        => lower.Contains("context_length_exceeded", StringComparison.Ordinal)
           || lower.Contains("maximum context", StringComparison.Ordinal)
           || lower.Contains("context length", StringComparison.Ordinal)
           || lower.Contains("prompt is too long", StringComparison.Ordinal)
           || lower.Contains("too many tokens", StringComparison.Ordinal);

    /// <summary>مدّة الانتظار من ترويسة <c>Retry-After</c> (ثوانٍ أو تاريخ).</summary>
    public static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? header = response.Headers.RetryAfter;
        if (header is null) return null;
        if (header.Delta is TimeSpan delta) return delta;
        if (header.Date is DateTimeOffset date)
        {
            TimeSpan wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }

    /// <summary>يستخرج <c>error.message</c> إن وُجد — للتشخيص فقط، لا يُعرض وحده للمستخدم.</summary>
    public static string? ExtractProviderMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out JsonElement err))
            {
                if (err.ValueKind == JsonValueKind.String) return err.GetString();
                if (err.ValueKind == JsonValueKind.Object
                    && err.TryGetProperty("message", out JsonElement m)
                    && m.ValueKind == JsonValueKind.String)
                    return m.GetString();
            }
        }
        catch (JsonException)
        {
            // ليس JSON — نعيد المقتطع الخام.
        }
        return body;
    }
}
