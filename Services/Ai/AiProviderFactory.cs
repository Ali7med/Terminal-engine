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

        AiProviderDescriptor? descriptor = DescriptorFor(settings);
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

    /// <summary>
    /// المدخلة الفعّالة: مدمجة من الكتالوج، أو مبنيّة من عنوان المستخدم إن اختار «مزوّد مخصّص».
    /// المخصّص يحتاج عنواناً صالحاً في الإعدادات، وإلّا يعيد null (لا مزوّد بلا وجهة).
    /// </summary>
    public static AiProviderDescriptor? DescriptorFor(AiSettings settings)
    {
        if (string.Equals(settings.ProviderId, AiProviderCatalog.CustomId, StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(settings.BaseUrlOverride)
                ? null
                : AiProviderCatalog.Custom(settings.BaseUrlOverride, settings.Model);
        }
        return AiProviderCatalog.Find(settings.ProviderId);
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
        return DescriptorFor(settings)?.DefaultModel ?? "";
    }
}
