using System;
using System.Security.Cryptography;
using System.Text;

namespace TerminalLauncher.Services;

/// <summary>
/// تعمية/فكّ الأسرار (كلمات مرور الخوادم/المفاتيح الخاصّة) عبر DPAPI المربوطة بحساب Windows الحاليّ
/// (<see cref="DataProtectionScope.CurrentUser"/>). النصّ المُعمّى base64 يُخزَّن في قاعدة البيانات؛
/// لا كلمات مرور خام على القرص. لا يُفكّ إلّا على نفس الحساب/الجهاز.
/// </summary>
public static class SecretProtector
{
    /// <summary>يعمّي نصّاً سرّياً إلى base64، أو null إن كان فارغاً.</summary>
    public static string? Encrypt(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        byte[] bytes = Encoding.UTF8.GetBytes(plain);
        byte[] enc = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(enc);
    }

    /// <summary>يفكّ نصّاً مُعمّى (base64)، أو null إن فشل الفكّ/كان فارغاً.</summary>
    public static string? Decrypt(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try
        {
            byte[] enc = Convert.FromBase64String(cipher);
            byte[] dec = ProtectedData.Unprotect(enc, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
        catch
        {
            return null; // سرّ مُعمّى بحساب/جهاز آخر، أو تالف
        }
    }
}
