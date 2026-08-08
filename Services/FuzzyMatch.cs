using System;
using System.Collections.Generic;

namespace TerminalLauncher.Services;

/// <summary>
/// مطابقة ضبابيّة بدرجات: تقبل أن تكون حروف الاستعلام <b>متفرّقة بالترتيب</b> داخل النصّ، فتُطابق
/// «الأحرف الأولى من الكلمات» و«جزءاً من كلمة» و«بادئة» جميعاً بقاعدةٍ واحدة.
///
/// <para><b>لماذا لا <c>Contains</c>:</b> التطابق الجزئيّ يشترط أن تكتب الحروف متلاصقةً كما هي في
/// النصّ، فلا يجد <c>dpa</c> في <c>docker ps -a</c> ولا <c>gcm</c> في <c>git commit -m</c> — وهذه
/// بالضبط الطريقة التي يتذكّر بها المرءُ أمراً طويلاً. المطابقة بالترتيب المتفرّق تجدها كلّها.</para>
///
/// <para><b>الدرجة لا الصواب/الخطأ:</b> كثيرٌ من المدخلات يطابق، والمهمّ أيّها أعلى. فنُكافئ
/// المطابقةَ على بداية كلمة، والحروفَ المتتالية، والمطابقةَ المبكّرة في النصّ — وهي مجتمعةً تقرّب
/// النتيجة من حَدْس المستخدم: ما كتبتُه أوّلَ حروفِ الكلمات يجب أن يتصدّر.</para>
/// </summary>
public static class FuzzyMatch
{
    /// <summary>لا تطابق.</summary>
    public const int NoMatch = -1;

    // أوزان مضبوطة بحيث تتقدّم المطابقة على بدايات الكلمات على المتفرّقة داخلها دائماً.
    private const int WordStartBonus = 14;   // الحرف عند بداية كلمة (أو بعد فاصل)
    private const int ConsecutiveBonus = 9;  // تلا الحرفَ السابق مباشرةً
    private const int LeadingPenalty = 2;    // خصمٌ لكلّ حرفٍ قبل أوّل تطابق (بحدّ أقصى)
    private const int MaxLeadingPenalty = 12;
    private const int FullPrefixBonus = 40;  // النصّ يبدأ بالاستعلام حرفيّاً
    private const int ExactBonus = 90;       // النصّ هو الاستعلام

    /// <summary>
    /// يطابق <paramref name="query"/> داخل <paramref name="text"/> ويعيد درجةً موجبة، أو
    /// <see cref="NoMatch"/> إن لم تظهر كلّ حروف الاستعلام بالترتيب. استعلامٌ فارغ يطابق بدرجة صفر.
    /// </summary>
    public static int Score(string query, string text)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        if (string.IsNullOrEmpty(text)) return NoMatch;

        // المسافات في الاستعلام فواصل بحث لا حروفاً: «doc ps» = مقطعان يُبحَث عنهما بالترتيب.
        query = query.Trim();
        if (query.Length == 0) return 0;

        string q = query.ToLowerInvariant();
        string t = text.ToLowerInvariant();

        if (string.Equals(q, t, StringComparison.Ordinal)) return ExactBonus + 60;

        int score = 0;
        if (t.StartsWith(q, StringComparison.Ordinal)) score += FullPrefixBonus;

        int ti = 0, firstHit = -1, streak = 0;
        foreach (char c in q)
        {
            if (c == ' ') { streak = 0; continue; }   // الفاصل لا يُطابَق، لكنّه يقطع التتابع

            int hit = IndexOfFrom(t, c, ti);
            if (hit < 0) return NoMatch;

            if (firstHit < 0) firstHit = hit;
            if (hit == ti && ti > 0) { score += ConsecutiveBonus; streak++; score += Math.Min(streak, 4); }
            else streak = 0;

            if (IsWordStart(t, hit)) score += WordStartBonus;
            score += 4;   // قيمة أساسيّة لكلّ حرفٍ طُوبق
            ti = hit + 1;
        }

        if (firstHit > 0) score -= Math.Min(firstHit * LeadingPenalty, MaxLeadingPenalty);

        // النصّ الأقصر عند تساوي المطابقة أدقّ: «ps» في «docker ps» أولى منها في سطرٍ طويل يحويها عرَضاً.
        score += Math.Max(0, 24 - t.Length / 8);
        return Math.Max(score, 1);
    }

    /// <summary>
    /// أفضل درجة بين العنوان وبقيّة النصّ: العنوان يزن أكثر، فلا يتصدّر أمرٌ طابق نصَّه الطويل
    /// عرَضاً على أمرٍ طابق عنوانَه قصداً.
    /// </summary>
    public static int ScoreEntry(string query, string title, string rest)
    {
        int inTitle = Score(query, title);
        int inRest = Score(query, rest);

        if (inTitle == NoMatch && inRest == NoMatch) return NoMatch;
        if (inTitle == NoMatch) return Math.Max(1, inRest / 2);
        if (inRest == NoMatch) return inTitle + 20;
        return inTitle + 20 + inRest / 6;
    }

    /// <summary>مواضع الحروف المطابقة في النصّ — لتظليلها في القائمة.</summary>
    public static IReadOnlyList<int> Highlights(string query, string text)
    {
        var hits = new List<int>();
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text)) return hits;

        string q = query.Trim().ToLowerInvariant();
        string t = text.ToLowerInvariant();

        int ti = 0;
        foreach (char c in q)
        {
            if (c == ' ') continue;
            int hit = IndexOfFrom(t, c, ti);
            if (hit < 0) { hits.Clear(); return hits; }   // لا تطابق ⇒ لا تظليل جزئيّ مضلّل
            hits.Add(hit);
            ti = hit + 1;
        }
        return hits;
    }

    private static int IndexOfFrom(string text, char c, int start)
    {
        for (int i = start; i < text.Length; i++)
            if (text[i] == c) return i;
        return -1;
    }

    /// <summary>هل الموضع بداية كلمة؟ (أوّل النصّ، أو بعد فاصل، أو بداية camelCase)</summary>
    private static bool IsWordStart(string text, int i)
    {
        if (i == 0) return true;
        char prev = text[i - 1];
        return prev is ' ' or '-' or '_' or '.' or '/' or '\\' or ':' or '=' or ',' or '|';
    }
}
