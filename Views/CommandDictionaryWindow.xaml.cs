using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalLauncher.Models;
using TerminalLauncher.Services;

namespace TerminalLauncher.Views;

/// <summary>
/// قاموس الأوامر: مكتبةُ أوامرٍ شخصيّة تُفتح باختصار، يُبحَث فيها بحثاً ضبابيّاً، ويُدرَج المختار
/// في تيرمنال العمل الحاليّ.
///
/// <para><b>الإدراج لا التنفيذ:</b> المختار يذهب إلى صندوق الأمر ليُقرأ ويُعدَّل ثمّ يُنفَّذ بـEnter.
/// أمرٌ محفوظٌ منذ شهرٍ قد يحمل مساراً أو وسيطاً لم يعد صالحاً، وتنفيذه بضغطةٍ واحدة يجعل الخطأ
/// أسرع من مراجعته. وهو نفس عقد «اختيارٌ ثمّ تنفيذ» المتّبع في اقتراحات الصندوق.</para>
///
/// <para><b>البحث في كلّ شيء، والترتيب بالعنوان:</b> يُمسَح العنوان والوسوم والشرح ونصّ الأمر،
/// لكنّ ما طابق العنوان يتقدّم — فلا يتصدّر أمرٌ طابق سطرَه الطويل عرَضاً على أمرٍ طابق اسمَه قصداً.</para>
/// </summary>
public partial class CommandDictionaryWindow : Window
{
    private readonly CommandDictionaryStore _store;

    /// <summary>المدخلة المعروضة في المحرّر (نسخةٌ عاملة — الأصل لا يُمَسّ حتّى الحفظ).</summary>
    private DictionaryCommand? _editing;

    /// <summary>يمنع حلقة «تحديث الحقول ⇒ TextChanged ⇒ حفظ ⇒ تحديث» أثناء ملء المحرّر.</summary>
    private bool _loadingEditor;

    /// <summary>الأمر الذي اختاره المستخدم للإدراج (null = أُغلقت بلا اختيار).</summary>
    public string? ChosenCommand { get; private set; }

    public CommandDictionaryWindow(CommandDictionaryStore store, string? seedCommand = null)
    {
        InitializeComponent();
        _store = store;

        FlowDirection = Loc.Flow;
        ApplyTexts();

        Loaded += (_, _) =>
        {
            Refresh();

            // أمرٌ مُمرَّر من التيرمنال ⇒ نفتح مباشرةً على مدخلةٍ جديدة تحمله، فالإضافة هي المقصد.
            if (!string.IsNullOrWhiteSpace(seedCommand)) StartNew(seedCommand!.Trim());
            else SearchInput.Focus();
        };
    }

    private void ApplyTexts()
    {
        Title = Loc.T("dict.title");
        TitleText.Text = Loc.T("dict.title");
        SubtitleText.Text = Loc.T("dict.subtitle");
        Theme.Placeholder.SetText(SearchInput, Loc.T("dict.searchHint"));

        LblTitle.Text = Loc.T("dict.f.title");
        LblCommand.Text = Loc.T("dict.f.command");
        LblDescription.Text = Loc.T("dict.f.description");
        LblTags.Text = Loc.T("dict.f.tags");

        NewButton.Content = Loc.T("dict.new");
        ImportButton.Content = Loc.T("dict.import");
        ExportButton.Content = Loc.T("dict.export");
        DeleteButton.Content = Loc.T("dict.delete");
        InsertButton.Content = Loc.T("dict.insert");
        CloseButton.Content = Loc.T("dict.close");
    }

    // ===== القائمة والبحث =====

    private void Refresh(string? selectId = null)
    {
        var results = _store.Search(SearchInput.Text);
        ResultList.ItemsSource = results;

        EmptyText.Text = _store.All.Count == 0 ? Loc.T("dict.emptyStore") : Loc.T("dict.emptySearch");
        EmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (results.Count == 0) { ShowEditor(null); return; }

        var pick = selectId is null ? results[0] : results.FirstOrDefault(r => r.Id == selectId) ?? results[0];
        ResultList.SelectedItem = pick;
        ResultList.ScrollIntoView(pick);
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    /// <summary>الأسهم تتنقّل النتائج من داخل حقل البحث — فلا يغادره المستخدم ليصل إليها.</summary>
    private void Search_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int n = ResultList.Items.Count;
        switch (e.Key)
        {
            case Key.Down when n > 0:
                ResultList.SelectedIndex = (ResultList.SelectedIndex + 1) % n;
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when n > 0:
                ResultList.SelectedIndex = (ResultList.SelectedIndex - 1 + n) % n;
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                InsertSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ShowEditor(ResultList.SelectedItem as DictionaryCommand);

    private void ResultList_DoubleClick(object sender, MouseButtonEventArgs e) => InsertSelected();

    // ===== المحرّر =====

    private void ShowEditor(DictionaryCommand? item)
    {
        _loadingEditor = true;
        _editing = item?.Clone();

        EditorPanel.IsEnabled = item is not null;
        FieldTitle.Text = item?.Title ?? "";
        FieldCommand.Text = item?.Command ?? "";
        FieldDescription.Text = item?.Description ?? "";
        FieldTags.Text = item is null ? "" : string.Join(" ", item.Tags);

        UsageText.Text = item is null || item.UseCount == 0
            ? ""
            : string.Format(Loc.T("dict.used"), item.UseCount);

        _loadingEditor = false;
    }

    /// <summary>
    /// كلّ تعديلٍ يُحفَظ فوراً — لا زرّ «حفظ». محرّرٌ صغيرٌ بأربعة حقول لا يحتمل خطوةً إضافيّة،
    /// وفقدان تعديلٍ لأنّ المستخدم أغلق النافذة أسوأ من حفظٍ لم يطلبه صراحةً.
    /// </summary>
    private void Field_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingEditor || _editing is null) return;

        _editing.Title = FieldTitle.Text.Trim();
        _editing.Command = FieldCommand.Text;
        _editing.Description = FieldDescription.Text.Trim();
        _editing.Tags = FieldTags.Text
            .Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _store.Update(_editing.Clone());
        RefreshListKeepingFocus();
    }

    /// <summary>يعيد بناء القائمة دون أن يسحب التركيز من الحقل الذي يُكتَب فيه.</summary>
    private void RefreshListKeepingFocus()
    {
        string? id = _editing?.Id;
        var focused = Keyboard.FocusedElement;

        var results = _store.Search(SearchInput.Text);
        ResultList.ItemsSource = results;
        EmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var pick = results.FirstOrDefault(r => r.Id == id);
        if (pick is not null)
        {
            _loadingEditor = true;      // اختيار القائمة يُطلق ShowEditor — لا نريده أن يمسح ما يُكتَب
            ResultList.SelectedItem = pick;
            _loadingEditor = false;
        }

        if (focused is IInputElement el) Keyboard.Focus(el);
    }

    private void New_Click(object sender, RoutedEventArgs e) => StartNew("");

    private void StartNew(string command)
    {
        var item = new DictionaryCommand
        {
            Title = string.IsNullOrWhiteSpace(command) ? Loc.T("dict.newTitle") : FirstLine(command),
            Command = command,
        };
        _store.Add(item);

        SearchInput.Clear();          // وإلّا اختفت المدخلة الجديدة خلف بحثٍ قديم لا يطابقها
        Refresh(item.Id);
        FieldTitle.Focus();
        FieldTitle.SelectAll();
    }

    /// <summary>أوّل سطرٍ من الأمر عنواناً مبدئيّاً — أقرب تخمينٍ لِما سيسمّيه المستخدم.</summary>
    private static string FirstLine(string command)
    {
        string first = command.Split('\n')[0].Trim();
        return first.Length <= 60 ? first : first[..60].TrimEnd() + "…";
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;

        string? choice = AppDialog.Confirm(this, Loc.T("dict.delete"),
            string.Format(Loc.T("dict.deleteConfirm"), _editing.Title),
            (Loc.T("dlg.delete"), "yes", DialogButtonKind.Danger),
            (Loc.T("dlg.cancel"), "no", DialogButtonKind.Neutral));
        if (choice != "yes") return;

        _store.Remove(_editing.Id);
        Refresh();
    }

    // ===== الإدراج =====

    private void Insert_Click(object sender, RoutedEventArgs e) => InsertSelected();

    private void InsertSelected()
    {
        if (ResultList.SelectedItem is not DictionaryCommand item) return;
        if (string.IsNullOrWhiteSpace(item.Command)) return;

        _store.NoteUsed(item.Id);
        ChosenCommand = item.Command;
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ===== التصدير والاستيراد =====

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_store.All.Count == 0)
        {
            AppDialog.Alert(this, Loc.T("dict.export"), Loc.T("dict.emptyStore"));
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.T("dict.export"),
            Filter = "JSON (*.json)|*.json",
            FileName = "command-dictionary.json",
            DefaultExt = ".json",
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            _store.ExportTo(dlg.FileName);
            AppDialog.Alert(this, Loc.T("dict.export"),
                string.Format(Loc.T("dict.exported"), _store.All.Count, dlg.FileName));
        }
        catch (Exception ex)
        {
            CrashReporter.Log(ex, "Dictionary.Export");
            AppDialog.Alert(this, Loc.T("dict.export"), ex.Message);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("dict.import"),
            Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var r = _store.ImportFrom(dlg.FileName);
            Refresh();
            AppDialog.Alert(this, Loc.T("dict.import"),
                string.Format(Loc.T("dict.imported"), r.Added, r.Updated, r.Skipped));
        }
        catch (Exception ex)
        {
            CrashReporter.Log(ex, "Dictionary.Import");
            AppDialog.Alert(this, Loc.T("dict.import"), ex.Message);
        }
    }
}
