using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TerminalLauncher.Controls;

/// <summary>
/// سلوك مرفق يجعل زرّاً (عادةً زرّ الثلاث نقاط <c>⋮</c> بنمط <c>KebabButton</c>) يفتح قائمة سياقيّة عند النقر:
/// إمّا قائمة الزرّ نفسه (<see cref="FrameworkElement.ContextMenu"/>)، وإلّا أقرب عنصر أبٍ يملك قائمة سياقيّة
/// (فيُعاد استعمال قائمة الكلك الأيمن نفسها للبطاقة بلا تكرار). يُضبط <c>PlacementTarget</c> على مالك القائمة
/// كي يبقى <c>DataContext</c> سليماً (مهمّ لعناصر القائمة داخل القوالب).
/// </summary>
public static class MenuButton
{
    /// <summary>فعّل السلوك على زرّ: <c>ctrl:MenuButton.IsMenuTrigger="True"</c>.</summary>
    public static readonly DependencyProperty IsMenuTriggerProperty =
        DependencyProperty.RegisterAttached("IsMenuTrigger", typeof(bool), typeof(MenuButton),
            new PropertyMetadata(false, OnIsMenuTriggerChanged));

    public static bool GetIsMenuTrigger(DependencyObject d) => (bool)d.GetValue(IsMenuTriggerProperty);
    public static void SetIsMenuTrigger(DependencyObject d, bool value) => d.SetValue(IsMenuTriggerProperty, value);

    private static void OnIsMenuTriggerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase btn) return;
        btn.Click -= OnClick;
        if (e.NewValue is true) btn.Click += OnClick;
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement btn) return;

        // قائمة الزرّ نفسه أوّلاً، وإلّا ابحث صعوداً عن أوّل عنصر يملك قائمة سياقيّة (قائمة الكلك الأيمن للبطاقة)
        var menu = btn.ContextMenu ?? FindAncestorMenu(btn);
        if (menu is null) return;

        // حدّد الصفّ الحاوي أوّلاً كي تعمل معالِجات القائمة المعتمدة على العنصر المحدَّد (SelectedItem)
        SelectContainingRow(btn);

        // نضع القائمة عند الزرّ نفسه: DataContext الزرّ = DataContext الصفّ، فتبقى بيانات العنصر سليمة
        menu.PlacementTarget = btn;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static ContextMenu? FindAncestorMenu(DependencyObject start)
    {
        for (var node = VisualTreeHelper.GetParent(start); node != null; node = VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement fe && fe.ContextMenu is { } menu)
                return menu;
        return null;
    }

    private static void SelectContainingRow(DependencyObject start)
    {
        for (var node = VisualTreeHelper.GetParent(start); node != null; node = VisualTreeHelper.GetParent(node))
        {
            switch (node)
            {
                case ListBoxItem lbi: lbi.IsSelected = true; return;
                case System.Windows.Controls.DataGridRow row: row.IsSelected = true; return;
            }
        }
    }
}
