using System;
using System.Collections.Generic;
using System.Linq;

namespace TerminalLauncher.Services.Ai;

/// <summary>أيّ محوّل يخدم هذه المدخلة.</summary>
public enum AiProviderKind
{
    /// <summary>مسار OpenAI-compatible (الغالبيّة العظمى).</summary>
    OpenAiCompat,

    /// <summary>Anthropic Messages API الأصليّ.</summary>
    Anthropic,
}

/// <summary>
/// مدخلة مزوّد جاهزة في الكتالوج: عنوان الخدمة ونموذج افتراضيّ وأعلام قدرات ورابط صفحة المفاتيح.
/// وجود مدخلة <b>مسمّاة</b> لكلّ منصّة (لا خيار «متوافق مع OpenAI» يتطلّب لصق عنوان) هو الفرق بين
/// «ندعم Kimi» و«يمكنك تهيئة Kimi».
/// </summary>
/// <param name="Id">معرّف ثابت يُخزَّن في الإعدادات (لا يُترجَم ولا يتغيّر).</param>
/// <param name="DisplayName">الاسم المعروض.</param>
/// <param name="Kind">المحوّل الذي يخدمها.</param>
/// <param name="BaseUrl">عنوان الأساس الافتراضيّ (قابل للتعديل من الإعدادات).</param>
/// <param name="DefaultModel">نموذج ابتدائيّ معقول — نقطة انطلاق لا قائمة نهائيّة.</param>
/// <param name="KeysUrl">صفحة إنشاء المفاتيح (يفتحها زرّ «احصل على مفتاح»). فارغ لمن لا يحتاج مفتاحاً.</param>
/// <param name="Capabilities">أعلام القدرات.</param>
public sealed record AiProviderDescriptor(
    string Id,
    string DisplayName,
    AiProviderKind Kind,
    string BaseUrl,
    string DefaultModel,
    string KeysUrl,
    AiCapabilities Capabilities);

/// <summary>
/// كتالوج المنصّات المدمج. إضافة منصّة جديدة متوافقة مع OpenAI = سطر واحد هنا، بلا كود جديد.
/// <para><b>ملاحظة عن النماذج الافتراضيّة:</b> معرّفات النماذج تتغيّر بوتيرة أسرع من إصدارات هذه
/// الأداة. المصدر الحقيقيّ هو <see cref="IAiProvider.ListModelsAsync"/> (زرّ «تحديث القائمة» في
/// الإعدادات)؛ ما هنا نقطة انطلاق تُغنِي المستخدم عن البحث في أوّل تشغيل ليس إلّا.</para>
/// </summary>
public static class AiProviderCatalog
{
    /// <summary>معرّف المزوّد المخصّص (Base URL يدويّ) — يُضاف في موجة لاحقة.</summary>
    public const string CustomId = "custom";

    private static readonly AiProviderDescriptor[] Entries =
    {
        new("openai", "OpenAI", AiProviderKind.OpenAiCompat,
            "https://api.openai.com/v1", "gpt-4o",
            "https://platform.openai.com/api-keys",
            new AiCapabilities { UsageInStream = true, ContextWindow = 128_000 }),

        new("anthropic", "Anthropic (Claude)", AiProviderKind.Anthropic,
            "https://api.anthropic.com/v1", "claude-sonnet-5",
            "https://console.anthropic.com/settings/keys",
            new AiCapabilities { UsageInStream = true, ContextWindow = 200_000 }),

        // Gemini عبر نقطة نهايته المتوافقة مع OpenAI — فلا حاجة لمحوّل ثالث.
        new("gemini", "Google Gemini", AiProviderKind.OpenAiCompat,
            "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-2.0-flash",
            "https://aistudio.google.com/apikey",
            new AiCapabilities { ContextWindow = 1_000_000 }),

        new("deepseek", "DeepSeek", AiProviderKind.OpenAiCompat,
            "https://api.deepseek.com/v1", "deepseek-chat",
            "https://platform.deepseek.com/api_keys",
            new AiCapabilities { UsageInStream = true, ContextWindow = 64_000 }),

        // Kimi — مطلوبة بالاسم من صاحب المشروع.
        new("kimi", "Kimi (Moonshot)", AiProviderKind.OpenAiCompat,
            "https://api.moonshot.ai/v1", "kimi-k2-0905-preview",
            "https://platform.moonshot.ai/console/api-keys",
            new AiCapabilities { UsageInStream = true, ContextWindow = 128_000 }),

        // Z.ai (GLM) — مطلوبة بالاسم من صاحب المشروع. مسار الـAPI يتضمّن paas/v4 (ليس /v1).
        new("zai", "Z.ai (GLM)", AiProviderKind.OpenAiCompat,
            "https://api.z.ai/api/paas/v4", "glm-4.6",
            "https://z.ai/manage-apikey/apikey-list",
            new AiCapabilities { ContextWindow = 128_000 }),

        new("xai", "xAI (Grok)", AiProviderKind.OpenAiCompat,
            "https://api.x.ai/v1", "grok-4",
            "https://console.x.ai",
            new AiCapabilities { UsageInStream = true, ContextWindow = 256_000 }),

        new("mistral", "Mistral", AiProviderKind.OpenAiCompat,
            "https://api.mistral.ai/v1", "mistral-large-latest",
            "https://console.mistral.ai/api-keys",
            new AiCapabilities { ContextWindow = 128_000 }),

        // بوّابة: مفتاح واحد يفتح عشرات النماذج — أسهل مسار لمستخدم جديد.
        new("openrouter", "OpenRouter", AiProviderKind.OpenAiCompat,
            "https://openrouter.ai/api/v1", "openai/gpt-4o",
            "https://openrouter.ai/keys",
            new AiCapabilities { UsageInStream = true, ContextWindow = 128_000 }),

        // محلّيّ بالكامل: بلا مفتاح وبلا إنترنت — مسار «جرّب الآن» في أوّل تشغيل.
        new("ollama", "Ollama (محلّي)", AiProviderKind.OpenAiCompat,
            "http://localhost:11434/v1", "llama3.2",
            "",
            new AiCapabilities { KeyOptional = true, ContextWindow = 8_000 }),
    };

    /// <summary>كلّ المدخلات المدمجة بترتيب العرض.</summary>
    public static IReadOnlyList<AiProviderDescriptor> All => Entries;

    /// <summary>المزوّد الافتراضيّ عند أوّل تشغيل (بوّابة بمفتاح واحد).</summary>
    public const string DefaultId = "openrouter";

    /// <summary>يجد مدخلة بمعرّفها، أو null إن لم توجد (معرّف قديم/مخصّص).</summary>
    public static AiProviderDescriptor? Find(string? id)
        => string.IsNullOrEmpty(id) ? null : Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>عنوان خدمة Ollama المحلّيّة المستعمَل في فحص التوفّر عند الطلب.</summary>
    public const string OllamaProbeUrl = "http://localhost:11434/api/tags";
}
