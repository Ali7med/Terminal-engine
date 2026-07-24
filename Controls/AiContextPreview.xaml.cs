using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TerminalLauncher.Services;
using TerminalLauncher.Services.Ai;

namespace TerminalLauncher.Controls;

/// <summary>
/// معاينة ما سيُرسَل إلى المزوّد. تظهر إمّا بطلب المستخدم («راجع قبل الإرسال») وإمّا <b>قسريّاً</b>
/// حين يحجب المُنقّح شيئاً فعلاً — لأنّ الحجب تخفيف ضرر لا ضمانة، والمستخدم في النقرة الخمسين لم
/// يقرأ البافر وقد يكون فيه سرّ انطبع للتوّ.
///
/// <para>ضدّ إجهاد الإنذارات: العرض لوحة مضمّنة تُبرز المحجوب وحده (لا نافذة كاملة)، ولكلّ عنصر
/// زرّ «ليس سرّاً» يحفظ بصمته فلا يوقفك مرّة أخرى — فتبقى الوقفة القسريّة نادرة وذات معنى.</para>
/// </summary>
public partial class AiContextPreview : UserControl
{
    /// <summary>صفّ في قائمة المحجوبات (نوعه وعيّنته المقنَّعة).</summary>
    /// <param name="Kind">وصف نوع السرّ.</param>
    /// <param name="Masked">العيّنة المقنَّعة.</param>
    /// <param name="Token">الرمز الأصليّ — في الذاكرة فقط، لزرّ «ليس سرّاً».</param>
    /// <param name="AllowLabel">تسمية زرّ الاستثناء.</param>
    public sealed record RedactedRow(string Kind, string Masked, string Token, string AllowLabel);

    private Action<string>? _onConfirm;
    private Action? _onCancel;
    private Action<string>? _onAllowToken;

    public AiContextPreview()
    {
        InitializeComponent();
        Loc.Changed += ApplyLanguage;
        Unloaded += (_, _) => Loc.Changed -= ApplyLanguage;
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        FlowDirection = Loc.Flow;
        ConfirmBtn.Content = Loc.T("ai.prev.send");
        CancelBtn.Content = Loc.T("ai.prev.cancel");
    }

    /// <summary>
    /// يعرض المقتطف. <paramref name="onAllowToken"/> يُستدعى حين يقرّ المستخدم أنّ رمزاً ليس سرّاً.
    /// </summary>
    public void Show(
        AiContextSnippet snippet,
        string composedPayload,
        Action<string> onConfirm,
        Action onCancel,
        Action<string> onAllowToken)
    {
        _onConfirm = onConfirm;
        _onCancel = onCancel;
        _onAllowToken = onAllowToken;

        HeadlineText.Text = snippet.ForcePreview
            ? string.Format(Loc.T("ai.prev.redacted"), snippet.Redacted.Count)
            : Loc.T("ai.prev.title");

        List<RedactedRow> rows = snippet.Redacted
            .Select(item => new RedactedRow(item.Kind, item.Masked, item.Token, Loc.T("ai.prev.notSecret")))
            .ToList();

        RedactedList.ItemsSource = rows;
        RedactedList.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        PayloadBox.Text = composedPayload;
        Visibility = Visibility.Visible;
    }

    /// <summary>يخفي اللوحة بلا إرسال.</summary>
    public void Hide() => Visibility = Visibility.Collapsed;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        string payload = PayloadBox.Text;
        Hide();
        _onConfirm?.Invoke(payload);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _onCancel?.Invoke();
    }

    /// <summary>
    /// «ليس سرّاً»: تُحفظ بصمة الرمز (لا الرمز) فلا يفرض معاينة مرّة أخرى. حفظ الرمز نفسه كان
    /// سيحوّل قائمة الاستثناءات إلى مخزن أسرار — وهو نقيض الغرض.
    /// </summary>
    private void AllowToken_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string token } button) return;

        _onAllowToken?.Invoke(token);
        button.IsEnabled = false;
        button.Content = Loc.T("ai.prev.allowed");
    }
}
