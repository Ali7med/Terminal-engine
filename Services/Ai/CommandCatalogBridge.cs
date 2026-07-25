using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Storage;
using TerminalLauncher.Models;

namespace TerminalLauncher.Services.Ai;

/// <summary>اقتراح حفظ أمر متكرّر في الكتالوج.</summary>
/// <param name="TemplateHash">بصمة القالب — مفتاح تسجيل القرار (اقتراح واحد لكلّ قالب).</param>
/// <param name="SuggestedCommand">الأمر المقترَح حفظه (أكثر صيغة ملموسة تكراراً).</param>
/// <param name="RunCount">كم مرّة نُفِّذ.</param>
/// <param name="Shell">صدفته إن عُرفت.</param>
public sealed record CatalogSuggestion(string TemplateHash, string SuggestedCommand, int RunCount, string? Shell);

/// <summary>
/// جسر بين قاعدة التعلّم و«كتالوج الأوامر» الموجود: يقترح حفظ الأوامر التي تكرّرت كثيراً وليست
/// في الكتالوج بعد.
///
/// <para><b>المطابقة بنفس المُطبِّع:</b> نُطبّع أوامر الكتالوج بنفس <see cref="CommandTemplate"/>
/// الذي يطبّع تنفيذات المستخدم، ونقارن البصمات — وإلّا اقترحنا حفظ أمر يملكه أصلاً بصيغة مختلفة
/// قليلاً.</para>
///
/// <para><b>اقتراح واحد لكلّ قالب، والرفض دائم:</b> تسجيل الاقتراح في <c>ai_suggestions</c> يمنع
/// تكراره؛ ورفض المستخدم يُخزَّن رفضاً دائماً. إعادة عرض اقتراح مرفوض أسرع طريق لإفقاد الثقة
/// بالاقتراحات كلّها.</para>
/// </summary>
public sealed class CommandCatalogBridge
{
    /// <summary>عتبة التكرار خلال المدّة قبل الاقتراح.</summary>
    public const int MinRuns = 5;

    /// <summary>مدّة النافذة الزمنيّة (أيام).</summary>
    public const int WithinDays = 30;

    private readonly Func<AiKnowledgeStore> _store;
    private readonly Func<IEnumerable<CommandEntry>> _catalog;
    private readonly Func<bool> _enabled;

    /// <param name="store">قاعدة المعرفة.</param>
    /// <param name="catalog">يعيد أوامر الكتالوج الحاليّة (للمطابقة).</param>
    /// <param name="enabled">علَم التعلّم — الجسر جزء منه.</param>
    public CommandCatalogBridge(
        Func<AiKnowledgeStore> store,
        Func<IEnumerable<CommandEntry>> catalog,
        Func<bool> enabled)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
    }

    /// <summary>
    /// يجد أفضل مرشّح لاقتراح حفظه في الكتالوج، أو null إن لا مرشّح مؤهّل. لا يرمي.
    /// </summary>
    public CatalogSuggestion? NextSuggestion()
    {
        if (!_enabled()) return null;

        try
        {
            AiKnowledgeStore store = _store();

            // بصمات ما في الكتالوج أصلاً — بنفس المُطبِّع.
            var catalogHashes = new HashSet<string>(
                _catalog()
                    .Where(e => !string.IsNullOrWhiteSpace(e.Command))
                    .Select(e => CommandTemplate.Normalize(e.Command).Hash),
                StringComparer.Ordinal);

            foreach (CommandStat candidate in store.CatalogCandidates(MinRuns, WithinDays, limit: 10))
            {
                if (catalogHashes.Contains(candidate.TemplateHash)) continue;   // يملكه أصلاً
                return new CatalogSuggestion(candidate.TemplateHash, candidate.Sample, candidate.RunCount, candidate.Shell);
            }
        }
        catch (Exception)
        {
            // قاعدة مقفلة/تالفة: الاقتراح ميزة مساعدة لا يجوز أن تُسقط شيئاً.
        }

        return null;
    }

    /// <summary>يسجّل عرض الاقتراح فلا يتكرّر لنفس القالب.</summary>
    public void MarkShown(CatalogSuggestion suggestion)
        => SafeWrite(store => store.RecordSuggestion("catalog", suggestion.TemplateHash, suggestion.SuggestedCommand));

    /// <summary>
    /// يحفظ الأمر في الكتالوج (قبول المستخدم) ويسجّل القبول. يعيد المدخلة الجديدة كي يضيفها
    /// المستدعي إلى مخزنه ويحفظه.
    /// </summary>
    public CommandEntry Accept(CatalogSuggestion suggestion, string name)
    {
        var entry = new CommandEntry
        {
            Name = string.IsNullOrWhiteSpace(name) ? DeriveName(suggestion.SuggestedCommand) : name.Trim(),
            Command = suggestion.SuggestedCommand,
            Shell = string.IsNullOrWhiteSpace(suggestion.Shell) ? "cmd" : MapShell(suggestion.Shell!),
        };

        SafeWrite(store =>
        {
            long id = store.RecordSuggestion("catalog", suggestion.TemplateHash, suggestion.SuggestedCommand);
            store.DecideSuggestion(id, SuggestionVerdict.Accepted);
        });

        return entry;
    }

    /// <summary>يسجّل رفضاً دائماً لهذا القالب.</summary>
    public void Reject(CatalogSuggestion suggestion)
        => SafeWrite(store =>
        {
            long id = store.RecordSuggestion("catalog", suggestion.TemplateHash, suggestion.SuggestedCommand);
            store.DecideSuggestion(id, SuggestionVerdict.Rejected);
        });

    /// <summary>اسم افتراضيّ من أوّل كلمتين ذواتَي معنى في الأمر.</summary>
    private static string DeriveName(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "أمر محفوظ",
            1 => words[0],
            _ => words[0] + " " + words[1],
        };
    }

    /// <summary>يطابق اسم صدفة قاعدة المعرفة إلى مفتاح صدفة الكتالوج (cmd/powershell/bash).</summary>
    private static string MapShell(string shell)
    {
        string s = shell.ToLowerInvariant();
        if (s.Contains("powershell") || s.Contains("pwsh")) return "powershell";
        if (s.Contains("bash") || s.Contains("zsh") || s.Contains("wsl")) return "bash";
        return "cmd";
    }

    private void SafeWrite(Action<AiKnowledgeStore> work)
    {
        try { work(_store()); }
        catch (Exception) { /* التخزين مساعد */ }
    }
}
