using System.Collections.Generic;

namespace TerminalLauncher.Services.Ai;

/// <summary>
/// تفضيلات طبقة الـAI المحفوظة ضمن <c>AppSettings</c>.
/// <para><b>المفاتيح لا تُخزَّن هنا نصّاً صريحاً أبداً</b>: <see cref="EncryptedKeys"/> يحمل نصّاً
/// مُعمّى بـDPAPI (base64) لكلّ مزوّد، ويُفكّ عند الحاجة عبر <see cref="AiKeyStore"/>.</para>
/// </summary>
public sealed class AiSettings
{
    /// <summary>معرّف المزوّد النشط من <see cref="AiProviderCatalog"/>.</summary>
    public string ProviderId { get; set; } = AiProviderCatalog.DefaultId;

    /// <summary>النموذج المختار. فارغ = استعمل النموذج الافتراضيّ لمدخلة الكتالوج.</summary>
    public string Model { get; set; } = "";

    /// <summary>عنوان أساس بديل (لمن يشغّل وكيلاً أو نسخة محلّيّة). فارغ = عنوان الكتالوج.</summary>
    public string BaseUrlOverride { get; set; } = "";

    /// <summary>
    /// المفاتيح المُعمّاة: معرّف المزوّد ← نصّ DPAPI بصيغة base64. لا تُفكّ إلّا على نفس الحساب
    /// والجهاز؛ نقل الإعدادات إلى جهاز آخر يجعلها غير قابلة للفكّ وهي <b>حالة متوقَّعة</b>
    /// يعالجها <see cref="AiKeyStore"/> بطلب إعادة الإدخال لا بالانهيار.
    /// </summary>
    public Dictionary<string, string> EncryptedKeys { get; set; } = new();

    /// <summary>تسجيل سلوك الاستعمال في قاعدة المعرفة المحلّيّة. يمكن إطفاؤه كلّيّاً.</summary>
    public bool LearningEnabled { get; set; } = true;

    /// <summary>
    /// إرسال «السياق المحيط» (مقتطف بافر التبويب) مع رسائل الدردشة. <b>معطَّل افتراضاً</b> —
    /// يُفعَّل بنقرة من رأس اللوحة. أفعال «اشرح هذا» و«أصلح آخر فاشل» لا تحتاجه: الفعل نفسه
    /// موافقة على مقتطفه المستهدف وحده.
    /// </summary>
    public bool AmbientContextEnabled { get; set; } = false;

    /// <summary>
    /// عرض المعاينة قبل كلّ إرسال يحمل سياقاً. يبدأ مفعَّلاً ويمكن إطفاؤه — لكنّ إطفاءه
    /// <b>لا يُلغي</b> المعاينة القسريّة حين يحجب المُنقّح شيئاً فعلاً.
    /// </summary>
    public bool AlwaysPreview { get; set; } = true;

    /// <summary>وضع هادئ: يُوقف رقاقة «اشرح هذا الخطأ؟» بعد الأوامر الفاشلة.</summary>
    public bool QuietMode { get; set; } = false;

    /// <summary>هل صُرِفت بطاقة أوّل التشغيل (اختار المستخدم مساراً أو أجّل)؟</summary>
    public bool FirstRunDismissed { get; set; } = false;

    /// <summary>سقف مقتطف السياق بالمحارف — قصّ من الأعلى مع علامة اقتطاع.</summary>
    public int ContextCharLimit { get; set; } = 8000;
}
