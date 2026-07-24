using System;

namespace TerminalLauncher.Services.Ai;

/// <summary>
/// يبني المزوّد النشط من الإعدادات. نقطة واحدة تعرف أيّ محوّل يخدم أيّ مدخلة كتالوج، فلا يتفرّع
/// المستدعون على نوع المزوّد.
/// </summary>
public static class AiProviderFactory
{
    /// <summary>
    /// ينشئ المزوّد النشط، أو null إن كانت المدخلة مجهولة أو نقص المفتاح لمن يحتاجه.
    /// </summary>
    public static IAiProvider? Create(AiSettings settings, AiKeyStore keys)
    {
        if (settings is null || keys is null) return null;

        AiProviderDescriptor? descriptor = AiProviderCatalog.Find(settings.ProviderId);
        if (descriptor is null) return null;

        string? key = keys.Get(descriptor.Id);
        if (key is null && !descriptor.Capabilities.KeyOptional) return null;

        string? baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrlOverride) ? null : settings.BaseUrlOverride;

        return descriptor.Kind switch
        {
            AiProviderKind.Anthropic => new AnthropicProvider(descriptor, key, baseUrl),
            _ => new OpenAiCompatProvider(descriptor, key, baseUrl),
        };
    }

    /// <summary>ينشئ مزوّداً لمدخلة ومفتاح محدَّدين — لزرّ «اختبار الاتّصال» قبل الحفظ.</summary>
    public static IAiProvider CreateFor(AiProviderDescriptor descriptor, string? apiKey, string? baseUrl = null)
        => descriptor.Kind switch
        {
            AiProviderKind.Anthropic => new AnthropicProvider(descriptor, apiKey, baseUrl),
            _ => new OpenAiCompatProvider(descriptor, apiKey, baseUrl),
        };

    /// <summary>
    /// النموذج الفعّال: اختيار المستخدم إن وُجد، وإلّا افتراضيّ مدخلة الكتالوج.
    /// </summary>
    public static string ResolveModel(AiSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Model)) return settings.Model.Trim();
        return AiProviderCatalog.Find(settings.ProviderId)?.DefaultModel ?? "";
    }
}
