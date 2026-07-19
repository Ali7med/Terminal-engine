using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TerminalLauncher.Services;

namespace TerminalLauncher.Views;

/// <summary>
/// محرّر إعدادات مدمج وذكيّ: يعرض <c>config.json</c> (إعدادات الواجهة) وإعدادات النظام بتلوين
/// يميّز المفتاح عن القيمة، مع تنسيق تلقائيّ وتحقّق حيّ من الصياغة (أقواس/فواصل/علامات) يُظهر
/// موضع الخطأ (سطر/عمود). حفظ تبويب الواجهة يطبّق فوراً؛ حفظ النظام يتطلّب إعادة تشغيل.
/// </summary>
public partial class ConfigEditorWindow : Window
{
    private readonly SettingsStore _settings = new();
    private bool _systemTab;
    private readonly DispatcherTimer _validateTimer;

    public ConfigEditorWindow()
    {
        InitializeComponent();

        FlowDirection = Loc.Flow;
        Title          = Loc.T("cfg.title");
        TabUi.Content     = Loc.T("cfg.tab.ui");
        TabSystem.Content = Loc.T("cfg.tab.system");
        FormatBtn.Content   = Loc.T("cfg.format");
        ValidateBtn.Content = Loc.T("cfg.validate");
        ReloadBtn.Content   = Loc.T("cfg.reload");
        SaveBtn.Content      = Loc.T("cfg.save");
        CloseBtn.Content     = Loc.T("cfg.close");

        Editor.SyntaxHighlighting = SyntaxHighlighting.JsonKeyValue();

        // تحقّق حيّ مؤجَّل: سحب الكتابة يُطلق أحداثاً كثيرة، فنتحقّق بعد سكون قصير.
        _validateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _validateTimer.Tick += (_, _) => { _validateTimer.Stop(); ValidateAndShow(); };
        Editor.TextChanged += (_, _) => { _validateTimer.Stop(); _validateTimer.Start(); };

        LoadTab(system: false);
    }

    /// <summary>يحمّل نصّ التبويب المطلوب من مصدره (ملفّ الواجهة أو قاعدة إعدادات النظام).</summary>
    private void LoadTab(bool system)
    {
        _systemTab = system;
        TabUi.IsChecked = !system;
        TabSystem.IsChecked = system;

        if (system)
        {
            Editor.Text = _settings.GetAppSettingsJson();
        }
        else
        {
            // اضمن وجود الملفّ دون إعادة كتابته إن كان موجوداً (كي لا نطمس تعليقات المستخدم).
            if (!File.Exists(FontManager.ConfigPath)) FontManager.Save();
            try { Editor.Text = File.ReadAllText(FontManager.ConfigPath); }
            catch { Editor.Text = "{}"; }
        }

        Editor.CaretOffset = 0;
        ValidateAndShow();
    }

    private void TabUi_Click(object sender, RoutedEventArgs e)
    { if (_systemTab) LoadTab(false); else TabUi.IsChecked = true; }

    private void TabSystem_Click(object sender, RoutedEventArgs e)
    { if (!_systemTab) LoadTab(true); else TabSystem.IsChecked = true; }

    // ===== التحقّق من الصياغة =====

    /// <summary>يحلّل نصّ المحرّر كـ JSON؛ يعيد true إن صحّ، وإلّا يملأ <paramref name="message"/> بسطر/عمود الخطأ.</summary>
    private bool TryValidate(out string message)
    {
        try
        {
            using var _ = JsonDocument.Parse(Editor.Text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            message = Loc.T("cfg.valid");
            return true;
        }
        catch (JsonException ex)
        {
            // مواضع System.Text.Json صفريّة الأساس → +1 لعرض بشريّ.
            long line = (ex.LineNumber ?? 0) + 1;
            long col  = (ex.BytePositionInLine ?? 0) + 1;
            string m = ex.Message;
            int cut = m.IndexOf(" LineNumber:", StringComparison.Ordinal);
            if (cut >= 0) m = m[..cut];
            message = string.Format(Loc.T("cfg.errAt"), line, col, m);
            return false;
        }
    }

    private bool ValidateAndShow()
    {
        bool valid = TryValidate(out string msg);
        ShowStatus(msg, valid);
        return valid;
    }

    private void ShowStatus(string text, bool ok)
    {
        StatusText.Text = text;
        StatusText.Foreground =
            (ok ? TryFindResource("Brush.Success") : TryFindResource("Brush.Danger")) as Brush
            ?? (ok ? Brushes.LightGreen : Brushes.OrangeRed);
    }

    // ===== الأزرار =====

    private void Validate_Click(object sender, RoutedEventArgs e) => ValidateAndShow();

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateAndShow()) return;   // لا تُنسّق نصّاً غير صالح (قد يُفسده)
        int caret = Editor.CaretOffset;
        Editor.Text = TextFormatter.Auto(Editor.Text, "config.json");
        Editor.CaretOffset = Math.Min(caret, Editor.Text.Length);
        ShowStatus(Loc.T("cfg.formatted"), true);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidate(out _))
        {
            ShowStatus(Loc.T("cfg.cantSaveInvalid"), false);
            return;
        }
        try
        {
            if (_systemTab)
            {
                _settings.SetAppSettingsJson(Editor.Text);
                ShowStatus(Loc.T("cfg.savedRestart"), true);
            }
            else
            {
                File.WriteAllText(FontManager.ConfigPath, Editor.Text);
                FontManager.ReloadAndApply();   // يطبّق الخطوط/الأحجام/الاستدارة حيّاً
                ShowStatus(Loc.T("cfg.saved"), true);
            }
        }
        catch (Exception ex)
        {
            ShowStatus(string.Format(Loc.T("cfg.errSave"), ex.Message), false);
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => LoadTab(_systemTab);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
