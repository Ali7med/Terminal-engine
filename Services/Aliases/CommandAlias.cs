using System;
using System.Collections.Generic;

namespace TerminalLauncher.Services.Aliases;

/// <summary>
/// متغيّر داخل اسم مستعار. يُكتب في الأوامر كـ<c>$name</c> أو <c>${name}</c>، ويأخذ قيمته من
/// وسائط الاستدعاء بترتيب التعريف.
/// </summary>
public sealed class AliasVariable
{
    /// <summary>الاسم بلا <c>$</c> — حروف وأرقام وشرطة سفليّة.</summary>
    public string Name { get; set; } = "";

    /// <summary>وصف قصير يظهر في المحرّر ورسائل الخطأ.</summary>
    public string Label { get; set; } = "";

    /// <summary>القيمة المستعمَلة حين لا يمرّر المستخدم وسيطاً. فارغ = لا افتراضيّ.</summary>
    public string Default { get; set; } = "";

    /// <summary>إن كان إلزاميّاً ولا وسيط له ولا افتراضيّ، يُمنَع التنفيذ برسالة تسمّيه.</summary>
    public bool Required { get; set; } = true;
}

/// <summary>
/// اسم مستعار: كلمة تُكتب في صندوق الأوامر فتتوسّع إلى سلسلة أوامر، مع متغيّرات تأخذ قيمها من
/// وسائط الاستدعاء.
///
/// <para><b>مثال:</b> الاسم <c>gpc</c> بمتغيّر واحد <c>var</c>، وأوامره
/// <c>git add .</c> ثمّ <c>git commit -m "$var"</c> ثمّ <c>git push</c>. فيصير
/// <c>gpc "first commit"</c> ثلاثة أوامر جاهزة.</para>
/// </summary>
public sealed class CommandAlias
{
    /// <summary>معرّف ثابت (يُولَّد عند الإنشاء) — التسمية قابلة للتغيير، والمعرّف لا.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>الكلمة المكتوبة في الصندوق (بلا مسافات).</summary>
    public string Name { get; set; } = "";

    /// <summary>وصف يظهر في القائمة والاقتراحات.</summary>
    public string Description { get; set; } = "";

    /// <summary>المتغيّرات بترتيب ربطها بالوسائط.</summary>
    public List<AliasVariable> Variables { get; set; } = new();

    /// <summary>الأوامر بترتيب التنفيذ (سطر لكلّ أمر).</summary>
    public List<string> Commands { get; set; } = new();

    /// <summary>
    /// اسم الصدفة التي يعمل فيها هذا الاسم المستعار (فارغ = كلّ الصدفات). مطابقة جزئيّة غير
    /// حسّاسة لحالة الأحرف — أمرُ bash في PowerShell خطأ صامت يستحقّ المنع.
    /// </summary>
    public string Shell { get; set; } = "";

    /// <summary>يعرض الأوامر ويطلب تأكيداً قبل إرسالها (للأسماء التي تلمس شيئاً مهمّاً).</summary>
    public bool ConfirmBeforeRun { get; set; }

    /// <summary>معطَّل = موجود في القائمة لكنّه لا يتوسّع.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>نسخة مستقلّة — المحرّر يعدّل نسخة ولا يلمس الأصل حتى الحفظ.</summary>
    public CommandAlias Clone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Shell = Shell,
        ConfirmBeforeRun = ConfirmBeforeRun,
        Enabled = Enabled,
        Commands = new List<string>(Commands),
        Variables = Variables.ConvertAll(v => new AliasVariable
        {
            Name = v.Name,
            Label = v.Label,
            Default = v.Default,
            Required = v.Required,
        }),
    };
}
