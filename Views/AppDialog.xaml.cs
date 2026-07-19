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

        Button? safe = null, first = null;
        foreach (var (label, key, kind) in options)
        {
            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(22, 10, 22, 10),
                Margin = new Thickness(10, 0, 0, 0),   // فراغ أماميّ فقط ⇒ الزرّ الأخير ملاصق للحافّة
                MinWidth = 96,
                FontSize = 13.5,
                FontWeight = FontWeights.Medium,
                Cursor = Cursors.Hand,
            };
            // للمحايد: نترك النمط الضمنيّ (implicit Button style) — لا نضبطه إلى null فيفقد ستايل الثيم
            if (kind == DialogButtonKind.Accent)
                btn.Style = (Style)Application.Current.FindResource("AccentButton");
            else if (kind == DialogButtonKind.Danger)
                btn.Style = (Style)Application.Current.FindResource("DangerButton");
            string k = key;
            btn.Click += (_, _) => { dlg._result = k; dlg.DialogResult = true; };
            dlg.ButtonPanel.Children.Add(btn);
            first ??= btn;
            if (kind != DialogButtonKind.Danger) safe = btn;   // آخر زرّ غير خطر = التركيز الآمن
        }

        // التركيز الافتراضيّ على الزرّ الآمن (إلغاء/محايد) لا الخطر — فـ Enter لا يحذف عن طريق الخطأ
        var focusTarget = safe ?? first;
        if (focusTarget != null)
        {
            focusTarget.IsDefault = true;
            dlg.Loaded += (_, _) => focusTarget.Focus();
        }

        dlg.ShowDialog();
        return dlg._result;
    }

    /// <summary>
    /// حوار سؤال بنصّ: يعرض حقل إدخال بقيمة ابتدائيّة مُظلَّلة ويعيد النصّ بعد التشذيب، أو <c>null</c>
    /// عند الإلغاء/Escape أو إن تُرك فارغاً. Enter يؤكّد مباشرةً (لا يحتاج الوصول للزرّ).
    /// </summary>
    public static string? Prompt(Window? owner, string title, string message, string initial, string okLabel)
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
        dlg.InputBox.Visibility = Visibility.Visible;
        dlg.InputBox.Text = initial ?? "";

        var cancel = new Button
        {
            Content = Services.Loc.T("dlg.cancel"),
            Padding = new Thickness(22, 10, 22, 10), Margin = new Thickness(10, 0, 0, 0),
            MinWidth = 96, FontSize = 13.5, Cursor = Cursors.Hand,
        };
        cancel.Click += (_, _) => { dlg._result = null; dlg.DialogResult = false; };

        var ok = new Button
        {
            Content = okLabel,
            Style = (Style)Application.Current.FindResource("AccentButton"),
            Padding = new Thickness(22, 10, 22, 10), Margin = new Thickness(10, 0, 0, 0),
            MinWidth = 96, FontSize = 13.5, Cursor = Cursors.Hand, IsDefault = true,
        };
        ok.Click += (_, _) => { dlg._result = dlg.InputBox.Text.Trim(); dlg.DialogResult = true; };

        dlg.ButtonPanel.Children.Add(cancel);
        dlg.ButtonPanel.Children.Add(ok);
        dlg.Loaded += (_, _) => { dlg.InputBox.Focus(); dlg.InputBox.SelectAll(); };

        dlg.ShowDialog();
        return string.IsNullOrWhiteSpace(dlg._result) ? null : dlg._result;
    }

    /// <summary>حوار تنبيه بزرّ «حسناً» واحد (بديل <c>MessageBox</c> ذي الزرّ الواحد).</summary>
    public static void Alert(Window? owner, string title, string message)
        => Confirm(owner, title, message, (Services.Loc.T("srv.ed.ok"), "ok", true));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _result = null; DialogResult = false; }
        base.OnKeyDown(e);
    }
}
