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
        if (e.NewValue is not true) return;
        btn.Click += OnClick;

        // تلميح مُعرَّب يتبع اللغة (لا يمكن ربطه من Setter داخل Style، فنضبطه هنا حيث السلوك مرفق أصلاً)
        if (btn.ToolTip is null)
        {
            btn.ToolTip = Services.Loc.T("ctx.options");
            void retitle() => btn.Dispatcher.Invoke(() => btn.ToolTip = Services.Loc.T("ctx.options"));
            Services.Loc.Changed += retitle;
            btn.Unloaded += (_, _) => Services.Loc.Changed -= retitle;
        }
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement btn) return;

        // قائمة الزرّ نفسه أوّلاً، وإلّا ابحث صعوداً عن أوّل عنصر يملك قائمة سياقيّة (قائمة الكلك الأيمن للبطاقة)
        var menu = btn.ContextMenu ?? FindAncestorMenu(btn);
        if (menu is null) return;

        // حدّد الصفّ الحاوي أوّلاً كي تعمل معالِجات القائمة المعتمدة على العنصر المحدَّد (SelectedItem)
        SelectContainingRow(btn);

        // نضع القائمة عند الزرّ نفسه: DataContext الزرّ = DataContext الصفّ، فتبقى بيانات العنصر سليمة.
        // القائمة مشتركة مع الكلك الأيمن، فنستعيد موضعها الأصليّ عند الإغلاق وإلّا فُتحت لاحقاً عند
        // زرّ ⋮ قديم بدل مؤشّر الفأرة.
        var oldPlacement = menu.Placement;
        var oldTarget = menu.PlacementTarget;
        void restore(object? s, System.EventArgs _)
        {
            menu.Closed -= restore;
            menu.Placement = oldPlacement;
            menu.PlacementTarget = oldTarget;
        }
        menu.Closed += restore;

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

    /// <summary>
    /// يجعل صفّ الزرّ هو المحدَّد <b>وحده</b>. الإفراغ أوّلاً ضروريّ: في قوائم الاختيار المتعدّد (Extended)
    /// يضيف <c>IsSelected = true</c> الصفَّ للتحديد القائم، فتطال إجراءاتُ القائمة (حذف مثلاً) صفوفاً
    /// حدّدها المستخدم سابقاً ولم يقصدها — والكلك الأيمن العاديّ يستبدل التحديد ولا يضيف إليه.
    /// </summary>
    private static void SelectContainingRow(DependencyObject start)
    {
        for (var node = VisualTreeHelper.GetParent(start); node != null; node = VisualTreeHelper.GetParent(node))
        {
            switch (node)
            {
                case ListBoxItem lbi:
                    (ItemsControl.ItemsControlFromItemContainer(lbi) as ListBox)?.UnselectAll();
                    lbi.IsSelected = true;
                    return;
                case DataGridRow row:
                    (ItemsControl.ItemsControlFromItemContainer(row) as DataGrid)?.UnselectAll();
                    row.IsSelected = true;
                    return;
            }
        }
    }
}
