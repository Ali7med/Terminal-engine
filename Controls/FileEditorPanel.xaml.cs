using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using TerminalLauncher.Services;

namespace TerminalLauncher.Controls;

/// <summary>
/// لوحة تحرير ملفّ داخل التاب (T-7): محرّر AvalonEdit بتلوين نحويّ حسب الامتداد،
/// تتبّع تعديل (نقطة ● + Ctrl+S للحفظ)، وطلب تأكيد ثلاثيّ عند الإغلاق مع تغييرات غير محفوظة.
/// كلّ النصوص المرئيّة معرَّبة عبر <see cref="Loc.T"/>. اللوحة مكتفية بذاتها ولا تلمس النافذة الرئيسة.
/// </summary>
public partial class FileEditorPanel : UserControl
{
    private string? _path;
    private bool _dirty;

    /// <summary>يُطلَق حين تنتهي اللوحة من الإغلاق — الحاوية تطوي عمود المحرّر عنده.</summary>
    public event Action? CloseRequested;

    /// <summary>تلميح زرّ الإغلاق (معرَّب) — يُربَط في XAML.</summary>
    public string CloseTip => Loc.T("editor.cancel");

    public FileEditorPanel()
    {
        InitializeComponent();
        Editor.TextChanged += Editor_TextChanged;
        Editor.PreviewKeyDown += Editor_PreviewKeyDown;

        // مؤشّر النصّ ولون الخلفية داخل منطقة الأرقام يتبعان الثيم الداكن.
        Editor.TextArea.TextView.LinkTextForegroundBrush = (Brush)FindResource("Brush.Accent");
    }

    /// <summary>هل توجد تغييرات غير محفوظة؟</summary>
    public bool IsDirty => _dirty;

    /// <summary>يفتح ملفّاً في المحرّر: يحمّل نصّه ويختار تلويناً نحويّاً حسب الامتداد.</summary>
    public void Open(string path)
    {
        _path = path;
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex)
        {
            text = "// " + ex.Message;
            _path = null;
        }

        Editor.TextChanged -= Editor_TextChanged;   // التحميل الأوّليّ لا يُعَدّ تعديلاً
        Editor.Text = text;
        Editor.TextChanged += Editor_TextChanged;

        Editor.SyntaxHighlighting = SyntaxHighlighting.ForPath(path);

        FileNameText.Text = Path.GetFileName(path);
        SetDirty(false);
        Editor.Focus();
        Editor.CaretOffset = 0;
        Editor.ScrollToHome();
    }

    private void Editor_TextChanged(object? sender, EventArgs e) => SetDirty(true);

    /// <summary>Ctrl+S يحفظ الملفّ ويمسح علامة التعديل (حين يكون المحرّر مركّزاً).</summary>
    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Save();
            e.Handled = true;
        }
    }

    /// <summary>يكتب المحتوى إلى القرص ويمسح علامة التعديل (يتجاهل بصمت إن لا مسار).</summary>
    private bool Save()
    {
        if (string.IsNullOrEmpty(_path)) return false;
        try
        {
            File.WriteAllText(_path, Editor.Text);
            SetDirty(false);
            return true;
        }
        catch (Exception ex)
        {
            TerminalLauncher.Views.AppDialog.Alert(Window.GetWindow(this), Loc.T("editor.save"), ex.Message);
            return false;
        }
    }

    private void SetDirty(bool value)
    {
        _dirty = value;
        DirtyDot.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => TryClose();

    /// <summary>
    /// يحاول إغلاق اللوحة: إن لا تعديل يُغلق فوراً؛ وإلّا يعرض تأكيداً ثلاثيّاً
    /// (حفظ / عدم الحفظ / إلغاء). «إلغاء» يُبقيها مفتوحة.
    /// </summary>
    public void TryClose()
    {
        if (!_dirty) { CloseRequested?.Invoke(); return; }

        switch (PromptUnsaved())
        {
            case UnsavedChoice.Save:
                if (Save()) CloseRequested?.Invoke();
                break;
            case UnsavedChoice.DontSave:
                SetDirty(false);
                CloseRequested?.Invoke();
                break;
            case UnsavedChoice.Cancel:
            default:
                break;   // يبقى مفتوحاً
        }
    }

    private enum UnsavedChoice { Save, DontSave, Cancel }

    /// <summary>
    /// يعرض حواراً ثلاثيّ الأزرار متوافقاً مع ثيم التطبيق ومُعرَّباً؛ يُبنى في الكود (بلا ملفّ منفصل)
    /// على نمط <see cref="PasteConfirmDialog"/>. يعيد اختيار المستخدم.
    /// </summary>
    private UnsavedChoice PromptUnsaved()
    {
        var result = UnsavedChoice.Cancel;

        var dlg = new Window
        {
            Title = Loc.T("editor.unsavedTitle"),
            SizeToContent = SizeToContent.Height,
            Width = 460,
            MinHeight = 150,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            FlowDirection = Loc.Flow,
            Background = (Brush)FindResource("Brush.Bg"),
            Foreground = (Brush)FindResource("Brush.Text"),
            Owner = Window.GetWindow(this),
        };
        try { dlg.FontFamily = (FontFamily)FindResource("Font.Ui"); } catch { }

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var message = new TextBlock
        {
            Text = Loc.T("editor.unsavedMessage"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
            FontSize = 13,
        };
        Grid.SetRow(message, 0);
        root.Children.Add(message);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Grid.SetRow(buttons, 1);

        Button MakeButton(string key, UnsavedChoice choice, bool accent)
        {
            var b = new Button
            {
                Content = Loc.T(key),
                MinWidth = 96,
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 0, 10, 0),
            };
            if (accent && TryFindResource("AccentButton") is Style s) b.Style = s;
            b.Click += (_, _) => { result = choice; dlg.DialogResult = true; };
            return b;
        }

        buttons.Children.Add(MakeButton("editor.save", UnsavedChoice.Save, accent: true));
        buttons.Children.Add(MakeButton("editor.dontSave", UnsavedChoice.DontSave, accent: false));
        buttons.Children.Add(MakeButton("editor.cancel", UnsavedChoice.Cancel, accent: false));
        root.Children.Add(buttons);

        dlg.Content = root;
        dlg.ShowDialog();
        return result;
    }
}
