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

    /// <summary>
    /// حفظ المحادثات على القرص لاسترجاعها لاحقاً. <b>معطَّل افتراضاً</b> — قرار خصوصيّة: بلا
    /// تفعيل صريح لا تُكتب أيّ دردشة إلى القرص (تبقى في الذاكرة حتى إغلاق التطبيق). عند التفعيل
    /// تمرّ كلّ محادثة عبر مُنقّح الأسرار قبل الكتابة.
    /// </summary>
    public bool SaveConversations { get; set; } = false;

    /// <summary>
    /// درجة العشوائيّة المُرسَلة مع كلّ نداء. الافتراضيّ منخفض عمداً: مساعد تيرمنال يُقاس بدقّة
    /// أوامره لا بتنوّعها، وقيمة عالية تنتج صياغات أوامر مبتكَرة وخاطئة.
    /// </summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>سقف توكنز الردّ. <c>0</c> = اترك القرار للمزوّد (لا نُرسل الحقل أصلاً).</summary>
    public int MaxTokens { get; set; } = 0;

    /// <summary>
    /// تعليمات المستخدم الخاصّة، تُضاف إلى بادئة النظام في كلّ محادثة (مثل «استعمل pwsh دائماً»
    /// أو «اشرح مختصراً»). فارغ = بلا إضافة.
    /// </summary>
    public string SystemPromptExtra { get; set; } = "";

    /// <summary>
    /// وضع الذكاء في صندوق الأوامر: الكتابة تذهب إلى المساعد بدل الصدفة. يُحفظ كي يبقى الوضع
    /// الذي اختاره المستخدم بين الجلسات.
    /// </summary>
    public bool ComposerAiMode { get; set; } = false;

    /// <summary>هل صُرِفت بطاقة ترحيب «جلسة تيرمنال جديدة»؟ (اختصارات أوّل تشغيل.)</summary>
    public bool WelcomeCardDismissed { get; set; } = false;

    /// <summary>
    /// تنفيذ الأمر الذي يقترحه المساعد في وضع الذكاء تلقائيّاً بدل الاكتفاء بإدراجه.
    /// <para><b>الأوامر الخطرة مستثناة دائماً</b> (<see cref="RiskyCommandDetector"/>): تُدرَج
    /// وتنتظر تأكيداً صريحاً مهما كان هذا الإعداد. وكذلك أيّ كتلة متعدّدة الأسطر — سكربت كامل
    /// ليس «أمراً مقترَحاً» يُنفَّذ بلا قراءة.</para>
    /// </summary>
    public bool AutoRunAiCommand { get; set; } = true;

    /// <summary>عرض لوحة الذكاء بالبكسل. القيمة تسري على كلّ التبويبات حيّاً.</summary>
    public double PanelWidth { get; set; } = 360;

    /// <summary>حجم نصّ المحادثة داخل اللوحة (منفصل عن حجم نصّ الواجهة العامّ).</summary>
    public double ChatFontSize { get; set; } = 12;
}
