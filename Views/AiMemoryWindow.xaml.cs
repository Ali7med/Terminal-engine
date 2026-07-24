using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Terminal.Storage;
using TerminalLauncher.Services;
using TerminalLauncher.Theme;

namespace TerminalLauncher.Views;

/// <summary>
/// «ذاكرة التطبيق» — الواجهة المرئيّة لقاعدة المعرفة المحلّيّة.
///
/// <para><b>لماذا نافذة من الدرجة الأولى:</b> تعلُّمٌ لا يراه المستخدم لا يمكن تصديقه ولا تصحيحه.
/// هنا يرى ما استُنتج عنه، ويحذف ما لا يريده، ويطفئ التسجيل كلّياً — والشفافيّة هي ما يجعل قبول
/// الالتقاط ممكناً أصلاً.</para>
///
/// <para><b>تحرير مقيَّد عمداً:</b> الأفعال المتاحة حذف/حظر/تثبيت — لا تحرير نصّ حرّ للإحصاءات،
/// وإلّا فسد اتّساق العدّادات مع ما جرى فعلاً. والحذف متسلسل (قالب ← اقتراحاته) كي لا يبقى
/// اقتراح يتيم عن موضوع حذفه المستخدم.</para>
/// </summary>
public partial class AiMemoryWindow : Window
{
    /// <summary>صفّ أمر معروض.</summary>
    public sealed record CommandRow(
        string Hash, string Template, int RunCount, string Outcome,
        string PinLabel, string BanLabel, string DeleteLabel);

    /// <summary>صفّ خطأ معروض.</summary>
    public sealed record ErrorRow(string Sample, string SolutionText, int SeenCount);

    /// <summary>صفّ اقتراح معروض.</summary>
    public sealed record SuggestionRow(string Payload, string Kind, string VerdictText);

    private readonly AiKnowledgeStore _store;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly Func<string> _profileText;
    private bool _syncing;

    private AiMemoryWindow(
        AiKnowledgeStore store, AppSettings settings, Action saveSettings, Func<string> profileText)
    {
        _store = store;
        _settings = settings;
        _saveSettings = saveSettings;
        _profileText = profileText;

        InitializeComponent();
        ApplyLanguage();
        Loc.Changed += ApplyLanguage;
        Closed += (_, _) => Loc.Changed -= ApplyLanguage;

        Reload();
    }

    /// <summary>يفتح النافذة فوق مالكها.</summary>
    /// <param name="profileText">
    /// يعيد ملفّ معرفة المستخدم <b>كما يُرسَل حرفياً</b> — من نفس مسار البانِي المخبَّأ، لا من
    /// محاكاة موازية تنحرف عمّا يصل المزوّد فعلاً.
    /// </param>
    public static void ShowFor(
        Window? owner, AiKnowledgeStore store, AppSettings settings, Action saveSettings, Func<string> profileText)
    {
        var window = new AiMemoryWindow(store, settings, saveSettings, profileText) { Owner = owner };
        window.ShowDialog();
    }

    private void ApplyLanguage()
    {
        FlowDirection = Loc.Flow;
        Title = Loc.T("ai.mem.title");
        TitleText.Text = Loc.T("ai.mem.title");
        SubtitleText.Text = Loc.T("ai.mem.subtitle");

        TabCommands.Content = Loc.T("ai.mem.tabCommands");
        TabErrors.Content = Loc.T("ai.mem.tabErrors");
        TabSuggestions.Content = Loc.T("ai.mem.tabSuggestions");
        TabProfile.Content = Loc.T("ai.mem.tabProfile");

        ColCommand.Header = Loc.T("ai.mem.colCommand");
        ColRuns.Header = Loc.T("ai.mem.colRuns");
        ColOutcome.Header = Loc.T("ai.mem.colOutcome");
        ColActions.Header = Loc.T("ai.mem.colActions");
        ColError.Header = Loc.T("ai.mem.colError");
        ColSolution.Header = Loc.T("ai.mem.colSolution");
        ColSeen.Header = Loc.T("ai.mem.colSeen");
        ColSuggestion.Header = Loc.T("ai.mem.colSuggestion");
        ColKind.Header = Loc.T("ai.mem.colKind");
        ColVerdict.Header = Loc.T("ai.mem.colVerdict");

        LearningCheck.Content = Loc.T("ai.set.learning");
        ClearAllBtn.Content = Loc.T("ai.mem.clearAll");
        CloseBtn.Content = Loc.T("ai.prev.cancel");

        Reload();
    }

    /// <summary>يعيد تحميل كلّ الأقسام من القاعدة ويحدّث الملخّص.</summary>
    private void Reload()
    {
        _syncing = true;
        LearningCheck.IsChecked = _settings.Ai.LearningEnabled;
        _syncing = false;

        IReadOnlyList<CommandStat> commands = SafeRead(() => _store.TopCommands(limit: 200), Array.Empty<CommandStat>());

        CommandsList.ItemsSource = commands.Select(ToRow).ToList();
        ErrorsList.ItemsSource = SafeRead(ReadErrors, new List<ErrorRow>());
        SuggestionsList.ItemsSource = SafeRead(ReadSuggestions, new List<SuggestionRow>());

        string profile = SafeRead(_profileText, "");
        ProfileBox.Text = profile.Length > 0 ? profile : Loc.T("ai.mem.profileEmpty");

        double? acceptance = SafeRead(() => _store.AcceptanceRate(), null);
        long bytes = SafeRead(() => _store.FileSize(), 0L);

        StatsText.Text = string.Format(
            CultureInfo.InvariantCulture,
            Loc.T("ai.mem.stats"),
            commands.Count,
            acceptance is double rate ? (rate * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "—",
            (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture));

        UpdateEmptyState();
    }

    private CommandRow ToRow(CommandStat stat)
    {
        string outcome = stat.FailCount == 0 && stat.SuccessCount == 0
            ? "—"
            : string.Format(CultureInfo.InvariantCulture, Loc.T("ai.mem.outcome"), stat.SuccessCount, stat.FailCount);

        return new CommandRow(
            stat.TemplateHash,
            stat.Template,
            stat.RunCount,
            outcome,
            Loc.T(stat.IsPinned ? "ai.mem.unpin" : "ai.mem.pin"),
            Loc.T(stat.IsBanned ? "ai.mem.unban" : "ai.mem.ban"),
            Loc.T("ai.mem.delete"));
    }

    /// <summary>
    /// الأخطاء تُقرأ من القوالب المعروفة: القاعدة لا تُصدِّر تعداداً عامّاً لها، فنعرض ما له حلّ
    /// محفوظ أو تكرّر — وهو ما يهمّ المستخدم فعلاً.
    /// </summary>
    private List<ErrorRow> ReadErrors()
    {
        var rows = new List<ErrorRow>();
        foreach (ErrorPattern pattern in _store.RecentErrors(limit: 200))
        {
            rows.Add(new ErrorRow(
                pattern.Sample,
                string.IsNullOrWhiteSpace(pattern.Solution) ? Loc.T("ai.mem.noSolution") : pattern.Solution!,
                pattern.SeenCount));
        }
        return rows;
    }

    private List<SuggestionRow> ReadSuggestions()
    {
        var rows = new List<SuggestionRow>();
        foreach ((string kind, string payload, SuggestionVerdict verdict) in _store.RecentSuggestions(limit: 200))
        {
            string text = verdict switch
            {
                SuggestionVerdict.Accepted => Loc.T("ai.mem.accepted"),
                SuggestionVerdict.Rejected => Loc.T("ai.mem.rejected"),
                _ => Loc.T("ai.mem.pending"),
            };
            rows.Add(new SuggestionRow(payload, kind, text));
        }
        return rows;
    }

    private void UpdateEmptyState()
    {
        // قسم الملفّ التعريفيّ يعرض نصّه دائماً (ولو رسالة «لا بيانات بعد») فلا حالة فارغة له.
        if (TabProfile.IsChecked == true)
        {
            EmptyText.Visibility = Visibility.Collapsed;
            return;
        }

        int count = ActiveList() switch
        {
            var list when list == CommandsList => CommandsList.Items.Count,
            var list when list == ErrorsList => ErrorsList.Items.Count,
            _ => SuggestionsList.Items.Count,
        };

        EmptyText.Text = Loc.T("ai.mem.empty");
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private ListView ActiveList()
        => TabErrors.IsChecked == true ? ErrorsList
         : TabSuggestions.IsChecked == true ? SuggestionsList
         : CommandsList;

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag }) return;

        TabCommands.IsChecked = tag == "commands";
        TabErrors.IsChecked = tag == "errors";
        TabSuggestions.IsChecked = tag == "suggestions";
        TabProfile.IsChecked = tag == "profile";

        CommandsList.Visibility = tag == "commands" ? Visibility.Visible : Visibility.Collapsed;
        ErrorsList.Visibility = tag == "errors" ? Visibility.Visible : Visibility.Collapsed;
        SuggestionsList.Visibility = tag == "suggestions" ? Visibility.Visible : Visibility.Collapsed;
        ProfileBox.Visibility = tag == "profile" ? Visibility.Visible : Visibility.Collapsed;

        UpdateEmptyState();
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => Toggle(sender, pin: true);
    private void Ban_Click(object sender, RoutedEventArgs e) => Toggle(sender, pin: false);

    private void Toggle(object sender, bool pin)
    {
        if (sender is not Button { Tag: string hash }) return;

        CommandStat? stat = FindStat(hash);
        if (stat is null) return;

        if (pin) _store.SetPinned(hash, !stat.IsPinned);
        else _store.SetBanned(hash, !stat.IsBanned);

        Reload();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hash }) return;

        _store.DeleteCommand(hash);
        Reload();
    }

    private CommandStat? FindStat(string hash)
        => SafeRead(() => _store.TopCommands(limit: 500), Array.Empty<CommandStat>())
            .FirstOrDefault(s => string.Equals(s.TemplateHash, hash, StringComparison.Ordinal));

    private void Learning_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _settings.Ai.LearningEnabled = LearningCheck.IsChecked == true;
        _saveSettings();
    }

    /// <summary>مسح كامل — فعل لا رجعة فيه، فيُطلب تأكيد صريح.</summary>
    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        string? choice = AppDialog.Confirm(
            this, Loc.T("ai.mem.clearAll"), Loc.T("ai.mem.clearConfirm"),
            (Loc.T("ai.mem.clearAll"), "erase", DialogButtonKind.Danger),
            (Loc.T("ai.prev.cancel"), "cancel", DialogButtonKind.Neutral));

        if (choice != "erase") return;

        _store.ClearAll();
        Reload();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>قراءة تتحمّل قاعدة مقفلة أو تالفة: النافذة عرضٌ لا يجوز أن يُسقط التطبيق.</summary>
    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception) { return fallback; }
    }
}
