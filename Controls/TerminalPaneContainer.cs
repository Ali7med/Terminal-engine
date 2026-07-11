using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TerminalLauncher.Controls;

/// <summary>
/// حاوية «أجزاء» داخل تبويب واحد: شجرة قابلة للانقسام أفقياً/عمودياً.
/// كل ورقة تلفّ <see cref="TerminalTabView"/> واحداً بإطار يبرز الجزء النشط.
/// العقد الداخلية شبكات (<see cref="Grid"/>) بينها <see cref="GridSplitter"/>.
/// </summary>
public sealed class TerminalPaneContainer : Grid
{
    private readonly List<TerminalPane> _panes = new();
    private TerminalPane? _activePane;

    /// <summary>يُطلَق حين يبقى التبويب بلا أجزاء (آخر جزء أُغلق) ليزيله المضيف.</summary>
    public event Action<TerminalPaneContainer>? Emptied;

    public TerminalPaneContainer(TerminalTabView first)
    {
        var pane = CreatePane(first);
        Children.Add(pane);
        SetActive(pane, focus: false);   // التركيز يمنحه OnLoaded؛ لا نخطفه هنا
    }

    /// <summary>عرض الجزء النشط الحاليّ (لتوجيه الأوامر إليه).</summary>
    public TerminalTabView? ActiveView => _activePane?.View;

    /// <summary>كل عروض الأجزاء (لتطبيق الخطّ/الإغلاق الجماعي).</summary>
    public IReadOnlyList<TerminalTabView> AllViews
    {
        get
        {
            var list = new List<TerminalTabView>(_panes.Count);
            foreach (var p in _panes) list.Add(p.View);
            return list;
        }
    }

    // ===== الإنشاء والتوجيه =====

    private TerminalPane CreatePane(TerminalTabView view)
    {
        var pane = new TerminalPane(view);
        // النقر يفعّل الجزء بصرياً فقط دون خطف التركيز: وإلّا يُسرَق التركيز من الكومبو/الأزرار
        // بمجرّد الضغط عليها (كان يمنع اختيار الصدفة من القائمة المنسدلة).
        pane.Activated += p => SetActive(p, focus: false);
        view.CloseRequested += _ => ClosePaneByView(pane);
        _panes.Add(pane);
        return pane;
    }

    /// <summary>يجعل الجزء المعطى نشطاً ويحدّث الإطارات؛ يمنحه التركيز فقط عند <paramref name="focus"/>.</summary>
    private void SetActive(TerminalPane pane, bool focus)
    {
        if (!ReferenceEquals(_activePane, pane))
        {
            if (_activePane != null) _activePane.IsActivePane = false;
            _activePane = pane;
            pane.IsActivePane = true;
        }
        // عند التفعيل بالنقر لا نُجبر التركيز: العنصر المنقور (كومبو/زرّ/العارض) يأخذه طبيعياً.
        if (focus) pane.View.FocusTerminal();
    }

    public void FocusActive() => _activePane?.View.FocusTerminal();

    /// <summary>يغلق الجزء الذي يلفّ العرض المعطى (زرّ ✕ الخاصّ بالجزء) أياً كان الجزء النشط.</summary>
    private void ClosePaneByView(TerminalPane pane)
    {
        if (_panes.Contains(pane)) ClosePane(pane);
    }

    // ===== الانقسام =====

    /// <summary>يقسم الجزء النشط ويضع فيه عرضاً جديداً بجانبه.</summary>
    public void Split(Orientation orientation, TerminalTabView newView)
    {
        var target = _activePane;
        if (target == null) return;

        var newPane = CreatePane(newView);
        var parent = (Grid)VisualTreeHelper.GetParent(target)!;
        int index = parent.Children.IndexOf(target);
        parent.Children.RemoveAt(index);

        var split = BuildSplitGrid(orientation, target, newPane);
        parent.Children.Insert(index, split);
        // في شبكة الأب حافِظ على نفس الخلية (Row/Column) الّتي كان يشغلها الجزء.
        SetRow(split, GetRow(target));
        SetColumn(split, GetColumn(target));

        SetActive(newPane, focus: true);   // تفعيل برمجيّ: امنح الجزء الجديد التركيز للكتابة فوراً
        UpdatePaneChrome();
    }

    /// <summary>يحدّث إطار كل الأجزاء: إطار مميِّز عند وجود تقسيم، ومسطّح بلا إطار للجزء الوحيد.</summary>
    private void UpdatePaneChrome()
    {
        bool split = _panes.Count > 1;
        foreach (var p in _panes) p.SetSplitChrome(split);
    }

    /// <summary>ينشئ شبكة تقسيم ثنائيّة (جزءان متساويان) بينها فاصل قابل للسحب.</summary>
    private Grid BuildSplitGrid(Orientation orientation, UIElement first, UIElement second)
    {
        var grid = new Grid();
        var splitter = new GridSplitter
        {
            Background = ThemeBrush("Brush.Border"),
            ShowsPreview = false,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        };

        if (orientation == Orientation.Vertical)
        {
            // انقسام عمودي = جنباً لجنب: [* | فاصل | *]. الفاصل في عموده الخاصّ (لا يتراكب مع
            // الجزء) فيلتقط السحب بلا منازعة العارض؛ MinWidth يمنع تصغير جزء إلى الصفر.
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 80 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 80 });
            SetColumn((FrameworkElement)first, 0);
            SetColumn(splitter, 1);
            SetColumn((FrameworkElement)second, 2);

            splitter.Width = 6;
            splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
            splitter.ResizeDirection = GridResizeDirection.Columns;
        }
        else
        {
            // انقسام أفقي = فوق/تحت: [* / فاصل / *].
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 60 });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 60 });
            SetRow((FrameworkElement)first, 0);
            SetRow(splitter, 1);
            SetRow((FrameworkElement)second, 2);

            splitter.Height = 6;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
            splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitter.ResizeDirection = GridResizeDirection.Rows;
        }

        grid.Children.Add(first);
        grid.Children.Add(second);
        grid.Children.Add(splitter);
        return grid;
    }

    // ===== الإغلاق =====

    /// <summary>يغلق الجزء النشط (ينظّف الـ PTY ثم يطوي الشبكة). إن كان آخر جزء يُطلق Emptied.</summary>
    public void CloseActivePane()
    {
        var pane = _activePane;
        if (pane == null) return;
        ClosePane(pane);
    }

    /// <summary>
    /// ينزع الجزء الذي يلفّ العرض من الشجرة <b>دون</b> إنهاء جلسته (للفصل إلى نافذة مستقلّة)، ويفكّ
    /// ارتباط العرض كي يُعاد استضافته في نافذة أخرى. يعيد true إن وُجد الجزء. إن كان آخر جزء يُطلق Emptied.
    /// </summary>
    public bool DetachView(TerminalTabView view)
    {
        var pane = _panes.Find(p => ReferenceEquals(p.View, view));
        if (pane == null) return false;
        pane.Child = null;              // فكّ ارتباط العرض عن الجزء كي يُعاد استضافته
        RemovePaneFromTree(pane);       // نفس طيّ الشجرة كالإغلاق لكن بلا إنهاء الجلسة
        return true;
    }

    private void ClosePane(TerminalPane pane)
    {
        pane.View.CloseSession(deleteHistory: true);   // إغلاق المستخدم للجزء يمسح تاريخ جلسته
        RemovePaneFromTree(pane);
    }

    /// <summary>يزيل الجزء من شجرة الأجزاء ويطوي شبكة التقسيم (مشترك بين الإغلاق والفصل).</summary>
    private void RemovePaneFromTree(TerminalPane pane)
    {
        _panes.Remove(pane);

        if (_panes.Count == 0)
        {
            Children.Clear();
            _activePane = null;
            Emptied?.Invoke(this);
            return;
        }

        // اطوِ شبكة التقسيم: استبدلها بالجزء الشقيق داخل جدّ الأب.
        var splitGrid = (Grid)VisualTreeHelper.GetParent(pane)!;
        UIElement? sibling = null;
        foreach (UIElement child in splitGrid.Children)
            if (!ReferenceEquals(child, pane) && child is not GridSplitter) { sibling = child; break; }

        splitGrid.Children.Clear();
        var grandParent = (Panel)VisualTreeHelper.GetParent(splitGrid)!;
        int index = grandParent.Children.IndexOf(splitGrid);
        int gRow = GetRow(splitGrid), gCol = GetColumn(splitGrid);
        grandParent.Children.RemoveAt(index);

        if (sibling != null)
        {
            grandParent.Children.Insert(index, sibling);
            SetRow(sibling, gRow);
            SetColumn(sibling, gCol);
        }

        // فعّل جزءاً باقياً (الأوّل المتبقّي، أو الشقيق إن كان ورقة).
        var next = sibling as TerminalPane ?? (_panes.Count > 0 ? _panes[0] : null);
        if (next != null) SetActive(next, focus: true);
        UpdatePaneChrome();
    }

    /// <summary>فرشاة ثيم آمنة قبل ارتباط العنصر بالشجرة البصرية (تقرأ موارد التطبيق).</summary>
    internal static Brush ThemeBrush(string key)
        => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>ورقة في شجرة الأجزاء: إطار حوله يبرز حين يكون نشطاً + يلفّ عرض التيرمنال.</summary>
public sealed class TerminalPane : Border
{
    public TerminalTabView View { get; }

    /// <summary>يُطلَق حين يُنقر داخل الجزء ليُفعَّل.</summary>
    public event Action<TerminalPane>? Activated;

    public TerminalPane(TerminalTabView view)
    {
        View = view;
        Child = view;
        BorderBrush = TerminalPaneContainer.ThemeBrush("Brush.Border");
        SetSplitChrome(false);   // جزء وحيد = بلا إطار (نافذة واحدة، لا «واحدة داخل واحدة»)
        // تفعيل الجزء بالنقر (قبل أن يبتلع العرض الحدث).
        PreviewMouseDown += (_, _) => Activated?.Invoke(this);
    }

    /// <summary>
    /// يضبط إطار الجزء حسب وجود تقسيم: عند التقسيم إطار خفيف مستدير قليلاً يميّز الأجزاء؛
    /// الجزء الوحيد يكون مسطّحاً بلا إطار ولا زوايا (فيبدو نافذةً واحدة لا مُتداخلة).
    /// </summary>
    public void SetSplitChrome(bool split)
    {
        BorderThickness = new Thickness(split ? 1 : 0);
        CornerRadius = new CornerRadius(split ? 4 : 0);
        Margin = new Thickness(split ? 1 : 0);
    }

    /// <summary>حين نشطاً: إطار باللكنة؛ وإلا إطار خفيف بلون الحدّ.</summary>
    public bool IsActivePane
    {
        set => BorderBrush = value
            ? TerminalPaneContainer.ThemeBrush("Brush.Accent")
            : TerminalPaneContainer.ThemeBrush("Brush.Border");
    }
}
