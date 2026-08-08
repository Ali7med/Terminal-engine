using System;
using System.Collections.Generic;

namespace TerminalLauncher.Models;

/// <summary>
/// مدخلة في قاموس الأوامر: أمرٌ محفوظ يُبحَث عنه ويُدرَج في التيرمنال الحاليّ.
///
/// <para><b>لماذا نوعٌ ثالث بجانب «الأوامر المحفوظة» و«الأسماء المستعارة»:</b> كلٌّ منها يجيب
/// سؤالاً مختلفاً. <see cref="CommandEntry"/> يفتح <b>تيرمنالاً جديداً</b> على مسارٍ ما، والاسم
/// المستعار يتوسّع إلى أمرٍ حين <b>تتذكّر اسمه</b> وتكتبه. والقاموس لِما <b>لا تحفظه</b>: تبحث عنه
/// بأيّ حرفٍ تذكره فيُدرَج في التيرمنال الذي أنت فيه — مكتبةُ رجوعٍ لا مشغّل ولا اختصار.</para>
/// </summary>
public sealed class DictionaryCommand
{
    /// <summary>معرّف ثابت — العنوان يتغيّر والمعرّف لا (يربط الاستيراد بالموجود).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>العنوان المعروض في القائمة — عليه يقع ثقلُ البحث.</summary>
    public string Title { get; set; } = "";

    /// <summary>الأمر كما يُدرَج في الصندوق (قد يكون متعدّد الأسطر).</summary>
    public string Command { get; set; } = "";

    /// <summary>شرحٌ قصير: ماذا يفعل ومتى يُستعمل.</summary>
    public string Description { get; set; } = "";

    /// <summary>وسوم للتجميع والتصفية (نصّ حرّ، بلا مسافات داخل الوسم).</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>الصدفة التي يصلح لها (فارغ = كلّها) — تنبيهٌ لا منع.</summary>
    public string Shell { get; set; } = "";

    /// <summary>عدد مرّات الإدراج — يرفع المتكرّر إلى أعلى النتائج المتساوية.</summary>
    public int UseCount { get; set; }

    /// <summary>آخر استعمال (ISO 8601 UTC، فارغ = لم يُستعمل).</summary>
    public string LastUsedUtc { get; set; } = "";

    /// <summary>نسخة مستقلّة — المحرّر يعدّل نسخةً ولا يمسّ الأصل حتّى الحفظ.</summary>
    public DictionaryCommand Clone() => new()
    {
        Id = Id,
        Title = Title,
        Command = Command,
        Description = Description,
        Shell = Shell,
        UseCount = UseCount,
        LastUsedUtc = LastUsedUtc,
        Tags = new List<string>(Tags),
    };

    /// <summary>النصّ الذي يمسحه البحث الذكيّ: العنوان ثمّ الوسوم ثمّ الشرح ثمّ الأمر.</summary>
    public string SearchHaystack => string.Join(" ", Title, string.Join(" ", Tags), Description, Command);
}
