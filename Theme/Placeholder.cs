using System.Windows;

namespace TerminalLauncher.Theme;

/// <summary>
/// نصّ إرشاديّ يظهر داخل حقل النصّ ما دام فارغاً ويختفي بأوّل حرف.
///
/// <para><b>لماذا خاصّيّة مرفقة لا عنصر فوق الحقل:</b> النمط المتكرّر في المشروع كان
/// <c>TextBlock</c> فوق الـ<c>TextBox</c> يُخفى يدويّاً في <c>TextChanged</c> — يعني عنصرين
/// وحدثاً ومعالجاً لكلّ حقل، وحقلاً واحداً منسيّاً يبقى نصّه الإرشاديّ فوق ما يكتبه المستخدم.
/// هنا القالب نفسه يتكفّل بالإظهار والإخفاء، فيكفي سطر واحد على الحقل — من XAML أو من الكود.</para>
///
/// <para><b>الاستعمال:</b> <c>&lt;TextBox theme:Placeholder.Text="مثال: gpc"/&gt;</c></para>
/// </summary>
public static class Placeholder
{
    /// <summary>النصّ الإرشاديّ. فارغ = لا نصّ (السلوك الافتراضيّ).</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Placeholder), new PropertyMetadata(""));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);
}
