using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Terminal.Storage;

namespace TerminalLauncher.Services.Ai;

/// <summary>عنصر واحد حُجب: نوعه وعيّنة مقنَّعة منه — لعرضه في المعاينة بلا كشف السرّ.</summary>
/// <param name="Kind">وصف النوع (مفتاح API، كلمة مرور، رمز عالي العشوائيّة، …).</param>
/// <param name="Masked">عيّنة مقنَّعة (أوّل محرفين + آخر محرفين).</param>
/// <param name="Token">الرمز الأصليّ — يبقى في الذاكرة فقط لزرّ «ليس سرّاً»؛ لا يُخزَّن ولا يُرسَل.</param>
public sealed record RedactedItem(string Kind, string Masked, string Token);

/// <summary>نتيجة تنقيح: النصّ بعد الحجب وقائمة ما حُجب.</summary>
/// <param name="Text">النصّ الآمن.</param>
/// <param name="Items">ما حُجب (فارغة = لم يُحجب شيء).</param>
public sealed record RedactionResult(string Text, IReadOnlyList<RedactedItem> Items)
{
    /// <summary>هل حُجب شيء؟ إن كانت true تُفرَض المعاينة قبل الإرسال مهما كانت الإعدادات.</summary>
    public bool AnythingRedacted => Items.Count > 0;
}

/// <summary>
/// حجب الأسرار قبل الإرسال وقبل التخزين. <b>تخفيف ضرر لا ضمانة</b> — ولذلك خطّ الدفاع الحقيقيّ
/// هو المعاينة القسريّة التي تُفرَض حين يحجب هذا المُنقّح شيئاً، لا الحجب وحده.
///
/// <para>ثلاث طبقات: (1) أنماط معروفة لبادئات المفاتيح وأزواج <c>key=value</c> الحسّاسة،
/// (2) استدلال إنتروبيا للرموز الطويلة عالية العشوائيّة بلا بادئة معروفة، (3) قيم مفاتيح المستخدم
/// المخزَّنة نفسها — فلو طبع أمرٌ مفتاحَ المستخدم في الخرج لم يعد إلى المزوّد.</para>
///
/// <para>خفض الإنذارات الكاذبة: الرموز الستّ‑عشريّة الصرفة (بصمات، معرّفات صور، هاشات) مستثناة من
/// طبقة الإنتروبيا — هي الأكثر شيوعاً في خرج البناء وأقلّها احتمالاً أن تكون سرّاً. وما يقرّ
/// المستخدم أنّه «ليس سرّاً» تُحفظ بصمته فلا يُحجب ثانيةً.</para>
/// </summary>
public sealed class SecretRedactor
{
    private const string Mask = "[محجوب]";

    /// <summary>أدنى طول لرمز يُفحص بالإنتروبيا (أقصر منه يكثر فيه الإنذار الكاذب).</summary>
    private const int MinEntropyLength = 24;

    /// <summary>أدنى إنتروبيا شانون (بت/محرف) لاعتبار الرمز مشبوهاً.</summary>
    private const double MinEntropyBits = 3.5;

    private static readonly (Regex Pattern, string Kind)[] Patterns =
    {
        // كتل المفاتيح الخاصّة — تُحجب كاملة.
        (new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----",
            RegexOptions.Compiled), "مفتاح خاص"),

        // JWT.
        (new Regex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b",
            RegexOptions.Compiled), "رمز JWT"),

        // بادئات مفاتيح معروفة.
        (new Regex(@"\b(?:sk|pk|rk)-[A-Za-z0-9_-]{16,}\b", RegexOptions.Compiled), "مفتاح API"),
        (new Regex(@"\bgh[pousr]_[A-Za-z0-9]{20,}\b", RegexOptions.Compiled), "رمز GitHub"),
        (new Regex(@"\bgithub_pat_[A-Za-z0-9_]{20,}\b", RegexOptions.Compiled), "رمز GitHub"),
        (new Regex(@"\bxox[baprs]-[A-Za-z0-9-]{10,}\b", RegexOptions.Compiled), "رمز Slack"),
        (new Regex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled), "مفتاح AWS"),
        (new Regex(@"\bAIza[0-9A-Za-z_-]{30,}\b", RegexOptions.Compiled), "مفتاح Google"),

        // Authorization: Bearer …
        (new Regex(@"(?i)\b(?:bearer|authorization:\s*bearer)\s+[A-Za-z0-9._~+/=-]{16,}",
            RegexOptions.Compiled), "ترويسة مصادقة"),

        // أزواج key=value / key: value الحسّاسة (تشمل --token=… و-p …).
        (new Regex(@"(?i)\b(?:api[_-]?key|apikey|access[_-]?token|auth[_-]?token|secret|client[_-]?secret|password|passwd|pwd|token)\b\s*[:=]\s*[""']?([^\s""'&;]{4,})",
            RegexOptions.Compiled), "قيمة حسّاسة"),

        // كلمة مرور في عنوان (user:pass@host).
        (new Regex(@"(?<=://)[^\s:/@]+:[^\s:/@]+(?=@)", RegexOptions.Compiled), "بيانات اعتماد في عنوان"),
    };

    private static readonly Regex HexOnly = new(@"\A[0-9a-fA-F]+\z", RegexOptions.Compiled);
    private static readonly Regex TokenCandidate = new(@"[A-Za-z0-9+/=_-]{24,}", RegexOptions.Compiled);

    private readonly Func<IEnumerable<string>> _storedKeys;
    private readonly Func<IReadOnlyCollection<string>> _allowedHashes;

    /// <param name="storedKeys">
    /// يعيد قيم مفاتيح المستخدم المخزَّنة (مفكوكة). تُحجب هي أيضاً: خرج التيرمنال قد يطبع مفتاح
    /// المستخدم نفسه، وإرساله للمزوّد تسريب لا يقلّ عن أيّ سرّ آخر.
    /// </param>
    /// <param name="allowedHashes">بصمات رموز أقرّ المستخدم أنّها ليست أسراراً.</param>
    public SecretRedactor(
        Func<IEnumerable<string>>? storedKeys = null,
        Func<IReadOnlyCollection<string>>? allowedHashes = null)
    {
        _storedKeys = storedKeys ?? Array.Empty<string>;
        _allowedHashes = allowedHashes ?? Array.Empty<string>;
    }

    /// <summary>ينقّح نصّاً ويعيد ما حُجب معه (للمعاينة).</summary>
    public RedactionResult Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return new RedactionResult(string.Empty, Array.Empty<RedactedItem>());

        IReadOnlyCollection<string> allowed = _allowedHashes();
        var items = new List<RedactedItem>();
        string text = input!;

        // (3) مفاتيح المستخدم المخزَّنة أوّلاً: مطابقة حرفيّة لا لبس فيها.
        foreach (string key in _storedKeys())
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length < 8) continue;
            if (!text.Contains(key, StringComparison.Ordinal)) continue;
            items.Add(new RedactedItem("مفتاحك المخزَّن", MaskOf(key), key));
            text = text.Replace(key, Mask, StringComparison.Ordinal);
        }

        // (1) الأنماط المعروفة.
        foreach ((Regex pattern, string kind) in Patterns)
        {
            text = pattern.Replace(text, match =>
            {
                // في أنماط الأزواج نحجب القيمة وحدها كي يبقى اسم المفتاح مفهوماً في السياق.
                Group target = match.Groups.Count > 1 && match.Groups[1].Success ? match.Groups[1] : match.Groups[0];
                string token = target.Value;

                if (IsAllowed(token, allowed)) return match.Value;

                items.Add(new RedactedItem(kind, MaskOf(token), token));
                return target == match.Groups[0]
                    ? Mask
                    : match.Value.Replace(token, Mask, StringComparison.Ordinal);
            });
        }

        // (2) استدلال الإنتروبيا لما لا بادئة معروفة له.
        text = TokenCandidate.Replace(text, match =>
        {
            string token = match.Value;
            if (!LooksSecret(token) || IsAllowed(token, allowed)) return token;
            items.Add(new RedactedItem("رمز عالي العشوائيّة", MaskOf(token), token));
            return Mask;
        });

        return new RedactionResult(text, items);
    }

    /// <summary>ينقّح ويعيد النصّ وحده — للتمرير إلى طبقة التخزين.</summary>
    public string RedactText(string? input) => Redact(input).Text;

    private static bool IsAllowed(string token, IReadOnlyCollection<string> allowed)
        => allowed.Count > 0 && allowed.Contains(CommandTemplate.Fingerprint(token));

    /// <summary>
    /// هل يبدو الرمز سرّاً؟ طويل، عالي العشوائيّة، ومختلط الفئات — مع استثناء الستّ‑عشريّ الصرف
    /// (بصمات وهاشات خرج البناء) لأنّه أكبر مصدر إنذارات كاذبة وأقلّها خطورة.
    /// </summary>
    private static bool LooksSecret(string token)
    {
        if (token.Length < MinEntropyLength) return false;
        if (HexOnly.IsMatch(token)) return false;

        bool hasLower = false, hasUpper = false, hasDigit = false;
        foreach (char c in token)
        {
            if (char.IsLower(c)) hasLower = true;
            else if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsDigit(c)) hasDigit = true;
        }
        if (!(hasDigit && (hasLower || hasUpper))) return false;

        return ShannonBits(token) >= MinEntropyBits;
    }

    /// <summary>إنتروبيا شانون بالبت لكلّ محرف.</summary>
    private static double ShannonBits(string text)
    {
        var counts = new Dictionary<char, int>();
        foreach (char c in text)
            counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;

        double bits = 0, length = text.Length;
        foreach (int count in counts.Values)
        {
            double p = count / length;
            bits -= p * Math.Log2(p);
        }
        return bits;
    }

    /// <summary>عيّنة مقنَّعة: أوّل محرفين وآخر محرفين — تكفي للتعرّف ولا تكشف.</summary>
    private static string MaskOf(string token)
        => token.Length <= 6
            ? new string('•', token.Length)
            : token[..2] + new string('•', Math.Min(8, token.Length - 4)) + token[^2..];
}
