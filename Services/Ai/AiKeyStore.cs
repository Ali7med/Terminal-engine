using System;
using System.Collections.Generic;

namespace TerminalLauncher.Services.Ai;

/// <summary>حالة مفتاح مزوّد على <b>هذا الجهاز</b>.</summary>
public enum AiKeyState
{
    /// <summary>لم يُدخَل مفتاح بعد.</summary>
    Missing,

    /// <summary>مفتاح صالح ومقروء.</summary>
    Present,

    /// <summary>
    /// يوجد نصّ مُعمّى لكنّه لا يُفكّ هنا — أُدخِل على حساب/جهاز آخر. حالة متوقَّعة لمن يعمل على
    /// أكثر من حاسوب، ويجب أن تُعالَج بطلب إعادة الإدخال لا برسالة «مفتاح خاطئ».
    /// </summary>
    NeedsReentry,
}

/// <summary>
/// مخزن مفاتيح المزوّدين فوق DPAPI (<see cref="SecretProtector"/>). النصّ المُعمّى يعيش في
/// <see cref="AiSettings.EncryptedKeys"/>؛ لا مفتاح خام على القرص إطلاقاً.
///
/// <para><b>تعدّد الأجهزة:</b> DPAPI مربوطة بحساب ويندوز والجهاز، فنقل الإعدادات إلى حاسوب آخر
/// يجعل الفكّ يفشل. هذا ليس عطلاً بل حالة يعالجها <see cref="StateOf"/> بإرجاع
/// <see cref="AiKeyState.NeedsReentry"/>. الخلط بينها وبين «مفتاح غير صالح» يُرسل المستخدم
/// لمطاردة مشكلة غير موجودة عند المزوّد، ويلوّث إحصاءات الأخطاء.</para>
///
/// <para><b>لا تسريب:</b> المفاتيح لا تُكتب في أيّ سجلّ ولا رسالة استثناء، وقيمها تُمرَّر إلى
/// <see cref="SecretRedactor"/> كي تُحجب لو ظهرت في خرج التيرمنال نفسه.</para>
/// </summary>
public sealed class AiKeyStore
{
    private readonly Func<AiSettings> _settings;
    private readonly Action _persist;

    /// <param name="settings">يعيد كائن الإعدادات الحيّ.</param>
    /// <param name="persist">يحفظ الإعدادات بعد أيّ تعديل.</param>
    public AiKeyStore(Func<AiSettings> settings, Action persist)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
    }

    /// <summary>حالة مفتاح مزوّد على هذا الجهاز.</summary>
    public AiKeyState StateOf(string providerId)
    {
        if (string.IsNullOrEmpty(providerId)) return AiKeyState.Missing;
        if (!_settings().EncryptedKeys.TryGetValue(providerId, out string? cipher) || string.IsNullOrEmpty(cipher))
            return AiKeyState.Missing;

        return SecretProtector.Decrypt(cipher) is null ? AiKeyState.NeedsReentry : AiKeyState.Present;
    }

    /// <summary>المفتاح المفكوك، أو null إن كان مفقوداً أو غير قابل للفكّ على هذا الجهاز.</summary>
    public string? Get(string providerId)
    {
        if (string.IsNullOrEmpty(providerId)) return null;
        return _settings().EncryptedKeys.TryGetValue(providerId, out string? cipher)
            ? SecretProtector.Decrypt(cipher)
            : null;
    }

    /// <summary>يخزّن مفتاحاً مُعمّى. قيمة فارغة تحذف المفتاح.</summary>
    public void Set(string providerId, string? plainKey)
    {
        if (string.IsNullOrEmpty(providerId)) return;

        AiSettings settings = _settings();
        if (string.IsNullOrWhiteSpace(plainKey))
        {
            settings.EncryptedKeys.Remove(providerId);
        }
        else
        {
            string? cipher = SecretProtector.Encrypt(plainKey!.Trim());
            if (cipher is null) return; // فشل التعمية — لا نكتب نصّاً صريحاً بديلاً أبداً
            settings.EncryptedKeys[providerId] = cipher;
        }
        _persist();
    }

    /// <summary>يحذف مفتاح مزوّد.</summary>
    public void Clear(string providerId) => Set(providerId, null);

    /// <summary>
    /// كلّ المفاتيح المفكوكة — يستهلكها <see cref="SecretRedactor"/> وحده كي يحجبها لو ظهرت في
    /// خرج التيرمنال. لا يُستعمل لأيّ غرض آخر.
    /// </summary>
    public IEnumerable<string> AllPlainKeys()
    {
        foreach (string cipher in _settings().EncryptedKeys.Values)
        {
            string? plain = SecretProtector.Decrypt(cipher);
            if (!string.IsNullOrWhiteSpace(plain))
                yield return plain!;
        }
    }
}
