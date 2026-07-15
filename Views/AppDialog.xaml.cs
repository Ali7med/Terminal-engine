using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TerminalLauncher.Views;

/// <summary>نوع زرّ الحوار: محايد (افتراضيّ) · لكنة بارز · خطر (أحمر — لإجراء لا رجعة فيه).</summary>
public enum DialogButtonKind { Neutral, Accent, Danger }

/// <summary>
/// حوار مخصّص بتصميم الثيم (بديل <c>MessageBox</c> الافتراضيّ) — نافذة بلا إطار، مركزيّة فوق مالكها،
/// بأزرار ديناميكيّة. تُستعمل من كلّ الأداة عبر <see cref="Confirm"/> (تُعيد مفتاح الزرّ) و<see cref="Alert"/>.
/// </summary>
public partial class AppDialog : Window
{
    private string? _result;

    private AppDialog() => InitializeComponent();

    /// <summary>
    /// يعرض حواراً بأزرار مخصّصة ويعيد مفتاح الزرّ المختار، أو <c>null</c> إن أُغلِق بـ Escape أو زرّ النظام.
    /// كلّ خيار: (نصّ الزرّ، مفتاح النتيجة، هل هو زرّ لكنة بارز). للأزرار الخطرة استعمل التحميل الزائد بـ
    /// <see cref="DialogButtonKind"/>.
    /// </summary>
    public static string? Confirm(Window? owner, string title, string message,
        params (string Label, string Key, bool Accent)[] options)
        => Confirm(owner, title, message,
            System.Array.ConvertAll(options, o => (o.Label, o.Key, o.Accent ? DialogButtonKind.Accent : DialogButtonKind.Neutral)));

    /// <summary>تحميل زائد يسمح بأزرار خطر (حمراء): كلّ خيار (نصّ، مفتاح، نوع الزرّ).</summary>
    public static string? Confirm(Window? owner, string title, string message,
        params (string Label, string Key, DialogButtonKind Kind)[] options)
    {
        var dlg = new AppDialog
        {
            Owner = owner,
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            FlowDirection = owner?.FlowDirection ?? FlowDirection.RightToLeft,
        };
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;

        foreach (var (label, key, kind) in options)
        {
            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 76,
                Cursor = Cursors.Hand,
            };
            btn.Style = kind switch
            {
                DialogButtonKind.Accent => (Style)Application.Current.FindResource("AccentButton"),
                DialogButtonKind.Danger => (Style)Application.Current.FindResource("DangerButton"),
                _ => null!,   // النمط الافتراضيّ (زرّ محايد)
            };
            string k = key;
            btn.Click += (_, _) => { dlg._result = k; dlg.DialogResult = true; };
            dlg.ButtonPanel.Children.Add(btn);
        }

        dlg.ShowDialog();
        return dlg._result;
    }

    /// <summary>حوار تنبيه بزرّ «حسناً» واحد (بديل <c>MessageBox</c> ذي الزرّ الواحد).</summary>
    public static void Alert(Window? owner, string title, string message)
        => Confirm(owner, title, message, ("حسناً", "ok", true));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _result = null; DialogResult = false; }
        base.OnKeyDown(e);
    }
}
