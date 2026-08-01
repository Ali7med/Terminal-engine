using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TerminalLauncher.Services;

namespace TerminalLauncher;

/// <summary>
/// شريط التبويبات: إعادة الترتيب بالسحب، والمجموعات (اسم + لون + طيّ).
///
/// <para>مفصولة عن <c>MainWindow.xaml.cs</c> — ذاك تجاوز ثلاثة آلاف سطر، وهذه ميزة قائمة بذاتها
/// لها حالتها الخاصّة.</para>
/// </summary>
public partial class MainWindow
{
    // ===== المجموعات =====

    /// <summary>
    /// اسم مجموعة التبويب ولونها — خاصّيتان مرفقتان تملكهما <see cref="Controls.TabStripPanel"/>:
    /// هي التي تحتاجهما للتخطيط ورسم الإطار، وتبقيان ملتصقتين بالتبويب مهما أُعيد ترتيبه.
    /// </summary>
    private static string GetGroup(DependencyObject o) => Controls.TabStripPanel.GetGroup(o);
    private static void SetGroup(DependencyObject o, string value) => Controls.TabStripPanel.SetGroup(o, value);

    /// <summary>بيانات مجموعة: اللون المعروض وحالة الطيّ. الاسم هو المفتاح في <see cref="_groups"/>.</summary>
    private sealed class TabGroup
    {
        public string Color = "";
        public bool Collapsed;
    }

    private readonly Dictionary<string, TabGroup> _groups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>تبويبات المجموعة بترتيب ظهورها في الشريط.</summary>
    private List<TabItem> GroupMembers(string name) =>
        TerminalTabs.Items.OfType<TabItem>()
            .Where(t => string.Equals(GetGroup(t), name, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>أسماء المجموعات المستعملة فعلاً (بترتيب أوّل ظهور) — المجموعة بلا أعضاء لا وجود لها.</summary>
    private List<string> ActiveGroups()
    {
        var seen = new List<string>();
        foreach (var tab in TerminalTabs.Items.OfType<TabItem>())
        {
            string g = GetGroup(tab);
            if (g.Length > 0 && !seen.Contains(g, StringComparer.OrdinalIgnoreCase)) seen.Add(g);
        }
        return seen;
    }

    /// <summary>
    /// يُسنِد تبويباً إلى مجموعة (اسم فارغ = إخراجه منها)، ثمّ يجمع أعضاء المجموعة متجاورين.
    ///
    /// <para>التجاور شرطٌ لا تجميل: المجموعة المطويّة تُخفي أعضاءها، فأعضاءٌ متفرّقون في الشريط
    /// يتركون فجوات في أماكن عشوائيّة.</para>
    /// </summary>
    private void AssignTabToGroup(TabItem tab, string groupName)
    {
        groupName = (groupName ?? "").Trim();
        SetGroup(tab, groupName);

        if (groupName.Length > 0)
        {
            if (!_groups.ContainsKey(groupName)) _groups[groupName] = new TabGroup();
            GatherGroup(groupName, tab);
        }

        PruneGroups();
        RefreshTabVisuals();
        if (!_restoring) SaveSession();
    }

    /// <summary>ينقل أعضاء المجموعة ليصيروا متجاورين خلف أوّل عضو (أو خلف <paramref name="anchor"/>).</summary>
    private void GatherGroup(string groupName, TabItem anchor)
    {
        var members = GroupMembers(groupName);
        if (members.Count < 2) return;

        int at = TerminalTabs.Items.IndexOf(members[0]);
        if (at < 0) at = TerminalTabs.Items.IndexOf(anchor);
        if (at < 0) return;

        foreach (var m in members)
        {
            int from = TerminalTabs.Items.IndexOf(m);
            if (from < 0 || from == at) { at++; continue; }
            MoveTabTo(m, at);
            at++;
        }
    }

    /// <summary>ينقل تبويباً إلى موضع مطلق في الشريط مع إبقاء التحديد كما هو.</summary>
    private void MoveTabTo(TabItem tab, int index)
    {
        int from = TerminalTabs.Items.IndexOf(tab);
        if (from < 0) return;

        index = Math.Clamp(index, 0, TerminalTabs.Items.Count - 1);
        if (index == from) return;

        bool wasSelected = ReferenceEquals(TerminalTabs.SelectedItem, tab);
        TerminalTabs.Items.RemoveAt(from);
        TerminalTabs.Items.Insert(index, tab);
        if (wasSelected) TerminalTabs.SelectedItem = tab;
    }

    /// <summary>يحذف المجموعات التي لم يبق لها عضو — وإلّا تراكمت أسماء لا يراها أحد.</summary>
    private void PruneGroups()
    {
        var alive = ActiveGroups();
        foreach (string dead in _groups.Keys.Where(k => !alive.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            _groups.Remove(dead);
    }

    /// <summary>
    /// يطوي/يفرد مجموعة. المطويّة تنكمش رؤوس أعضائها إلى الصفر ويبقى إطارها في موضعه حاملاً
    /// اسمها وعددها — نقل التحديد خارجها تتكفّل به <see cref="RefreshTabVisuals"/>.
    /// </summary>
    private void ToggleGroupCollapsed(string groupName)
    {
        if (!_groups.TryGetValue(groupName, out var g)) return;
        g.Collapsed = !g.Collapsed;

        RefreshTabVisuals();
        if (!_restoring) SaveSession();
    }

    /// <summary>يعيد تسمية مجموعة في كلّ أعضائها (الاسم هو الهويّة).</summary>
    private void RenameGroup(string oldName, string newName)
    {
        newName = (newName ?? "").Trim();
        if (newName.Length == 0 || string.Equals(oldName, newName, StringComparison.Ordinal)) return;

        if (_groups.Remove(oldName, out var g)) _groups[newName] = _groups.TryGetValue(newName, out var t) ? t : g;
        foreach (var tab in GroupMembers(oldName)) SetGroup(tab, newName);

        RefreshTabVisuals();
        if (!_restoring) SaveSession();
    }

    private void SetGroupColor(string groupName, string hex)
    {
        if (!_groups.TryGetValue(groupName, out var g)) return;
        g.Color = hex ?? "";
        RefreshTabVisuals();
        if (!_restoring) SaveSession();
    }

    // ===== رسم الرؤوس (إطار المجموعة تُعنى به TabStripPanel) =====

    /// <summary>
    /// يُزامن ما تحتاجه لوحة الشريط لرسم المجموعات: الاسم واللون على كلّ رأس، وأسماء المطويّات،
    /// ثمّ انكماش رؤوس أعضائها.
    ///
    /// <para>الترتيب مقصود: أسماء المطويّات تُبلَّغ اللوحة <b>قبل</b> بدء الحركة، فيأخذ الإطار
    /// صورته المطويّة من أوّل إطار وينكمش انكماشاً متّصلاً بلا قفزةٍ في نهايته.</para>
    /// </summary>
    private void RefreshTabVisuals()
    {
        var collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in ActiveGroups())
            if (_groups.TryGetValue(name, out var g) && g.Collapsed) collapsed.Add(name);

        if (TabStrip is { } strip) strip.CollapsedGroups = collapsed;

        foreach (var tab in TerminalTabs.Items.OfType<TabItem>())
        {
            string groupName = GetGroup(tab);
            _groups.TryGetValue(groupName, out var group);

            Controls.TabStripPanel.SetGroupColor(tab, group?.Color ?? "");
            // المطويّة تُخفي رؤوس أعضائها كلّها — الإطار وحده يمثّلها. المحتوى يبقى حيّاً.
            AnimateCollapse(tab, group?.Collapsed == true);
        }

        TabStrip?.InvalidateMeasure();

        // التحديد داخل مجموعة مطويّة يترك الشريط بلا تبويب مضاء — ننقله فوراً لأوّل مفرود.
        // المعيار حالة المجموعة لا ظهور الرأس: الرأس يبقى ظاهراً طوال الانكماش.
        if (TerminalTabs.SelectedItem is TabItem sel && collapsed.Contains(GetGroup(sel)))
        {
            var open = TerminalTabs.Items.OfType<TabItem>().FirstOrDefault(t => !collapsed.Contains(GetGroup(t)));
            if (open != null) TerminalTabs.SelectedItem = open;
        }
    }

    /// <summary>مدّة انكماش/انفراد الرأس. بطيئة عمداً بما يكفي لتتبعها العين وتفهم أين ذهبت التبويبات.</summary>
    private static readonly Duration CollapseDuration = new(TimeSpan.FromMilliseconds(520));

    /// <summary>
    /// يطوي/يفرد رأس تبويب بانسياب: العرض ينكمش إلى الصفر والعتامة تخفت معه، ثمّ يُخفى فعليّاً في
    /// النهاية. الإخفاء الفوريّ كان يبتلع التبويبات دفعةً واحدة فلا يُدرَك ماذا حدث.
    /// </summary>
    private static void AnimateCollapse(TabItem tab, bool collapsed)
    {
        double from = Controls.TabStripPanel.GetCollapseProgress(tab);
        double to = collapsed ? 1 : 0;

        if (!collapsed) tab.Visibility = Visibility.Visible;
        if (Math.Abs(from - to) < 0.01)
        {
            tab.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        var ease = new CubicEase { EasingMode = collapsed ? EasingMode.EaseIn : EasingMode.EaseOut };

        var width = new DoubleAnimation(from, to, CollapseDuration) { EasingFunction = ease };
        width.Completed += (_, _) =>
        {
            if (Controls.TabStripPanel.GetCollapseProgress(tab) > 0.99) tab.Visibility = Visibility.Collapsed;
        };

        tab.BeginAnimation(Controls.TabStripPanel.CollapseProgressProperty, width);
        tab.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(collapsed ? 0 : 1, CollapseDuration) { EasingFunction = ease });
    }
    /// <summary>لوحة الشريط داخل قالب <c>TabControl</c> (تُبحَث مرّةً وتُخبَّأ).</summary>
    private Controls.TabStripPanel? TabStrip
    {
        get
        {
            if (_tabStrip != null) return _tabStrip;
            _tabStrip = FindVisualChild<Controls.TabStripPanel>(TerminalTabs);
            if (_tabStrip != null)
            {
                _tabStrip.PreviewMouseLeftButtonDown += TabStrip_MouseDown;
                _tabStrip.PreviewMouseRightButtonDown += TabStrip_RightClick;
            }
            return _tabStrip;
        }
    }

    private Controls.TabStripPanel? _tabStrip;

    private static T? FindVisualChild<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null) return null;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (FindVisualChild<T>(child) is { } deep) return deep;
        }
        return null;
    }
    // ===== إعادة الترتيب بالسحب =====

    private TabItem? _dragTab;             // التبويب المسحوب (null = لا سحب)
    private Point _dragStart;              // نقطة الضغط — لعتبة البدء
    private double _dragOffset;            // إزاحة الرأس المسحوب أفقيّاً
    private bool _dragging;                // تجاوزنا العتبة فعلاً
    private int _dragTargetIndex = -1;     // الموضع الذي سيهبط فيه

    /// <summary>مسافة يجب تجاوزها قبل عدّ الحركة سحباً — وإلّا صارت كلّ نقرة سحباً مرتجفاً.</summary>
    private const double DragThreshold = 6;

    /// <summary>يربط مقابض السحب برأس تبويب جديد. يُستدعى من <c>OpenTerminal</c>.</summary>
    private void AttachTabDrag(TabItem tab)
    {
        tab.PreviewMouseLeftButtonDown += TabHeader_MouseDown;
        tab.PreviewMouseMove += TabHeader_MouseMove;
        tab.PreviewMouseLeftButtonUp += TabHeader_MouseUp;
        tab.LostMouseCapture += (_, _) => CancelTabDrag();
    }

    /// <summary>
    /// النقر على اسم مجموعة (أو على حبّتها المطويّة) يطويها/يفردها. يُركَّب على الشريط نفسه لأنّ
    /// الاسم والحبّة يرسمهما <see cref="Controls.TabStripPanel"/> ولا وجود لهما كعناصر تُنقَر.
    /// </summary>
    private void TabStrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Controls.TabStripPanel strip) return;
        if (strip.GroupLabelAt(e.GetPosition(strip)) is not { } group) return;

        ToggleGroupCollapsed(group);
        e.Handled = true;
    }

    /// <summary>
    /// الزرّ الأيمن على اسم المجموعة: تسمية · لون · طيّ/فرد · إغلاق المجموعة كلّها. الاسم يرسمه
    /// الشريط ولا وجود له كعنصر، فالقائمة تُبنى هنا وتُفتح عند نقطة النقر.
    /// </summary>
    private void TabStrip_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Controls.TabStripPanel strip) return;
        if (strip.GroupLabelAt(e.GetPosition(strip)) is not { } group) return;

        var menu = new ContextMenu { FlowDirection = Loc.Flow, PlacementTarget = strip };

        MenuItem Item(string key, Action act)
        {
            var mi = new MenuItem { Header = Loc.T(key) };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
            return mi;
        }

        Item("tabgrp.rename", () =>
        {
            string? name = Views.AppDialog.Prompt(this, Loc.T("tabgrp.rename"), Loc.T("tabgrp.namePrompt"),
                group, Loc.T("dlg.save"));
            if (!string.IsNullOrWhiteSpace(name)) RenameGroup(group, name!);
        });

        bool collapsed = _groups.TryGetValue(group, out var g) && g.Collapsed;
        Item(collapsed ? "tabgrp.expand" : "tabgrp.collapse", () => ToggleGroupCollapsed(group));

        menu.Items.Add(new Separator());
        Item("tabgrp.close", () => CloseGroup(group));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildColorRow(menu, hex => SetGroupColor(group, hex)));

        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>يغلق كلّ تبويبات المجموعة بعد تأكيد — الإغلاق الجماعيّ لا رجعة فيه.</summary>
    private void CloseGroup(string group)
    {
        var members = GroupMembers(group);
        if (members.Count == 0) return;

        string? choice = Views.AppDialog.Confirm(this, Loc.T("tabgrp.close"),
            string.Format(Loc.T("tabgrp.closeConfirm"), group, members.Count),
            (Loc.T("tabgrp.close"), "close", Views.DialogButtonKind.Danger),
            (Loc.T("ai.prev.cancel"), "cancel", Views.DialogButtonKind.Neutral));
        if (choice != "close") return;

        foreach (var tab in members) CloseTab(tab);
        PruneGroups();
        RefreshTabVisuals();
    }

    private void TabHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TabItem tab) return;
        _dragTab = tab;
        _dragStart = e.GetPosition(TerminalTabs);
        _dragging = false;
        _dragOffset = 0;
        _dragTargetIndex = TerminalTabs.Items.IndexOf(tab);
    }

    private void TabHeader_MouseMove(object sender, MouseEventArgs e)
    {
        var tab = _dragTab;
        if (tab is null || e.LeftButton != MouseButtonState.Pressed) return;

        Point now = e.GetPosition(TerminalTabs);
        double dx = now.X - _dragStart.X;

        if (!_dragging)
        {
            if (Math.Abs(dx) < DragThreshold) return;

            // CaptureMouse قد يفشل أو يُطلق LostMouseCapture فوراً — والمقبض المربوط عليه يُلغي
            // السحب ويُصفّر _dragTab تحت أقدامنا. نتحقّق بعده قبل أن نمسّ الرأس، وإلّا لمسنا null.
            if (!tab.CaptureMouse() || !ReferenceEquals(_dragTab, tab)) { CancelTabDrag(); return; }

            _dragging = true;
            tab.Opacity = 0.85;
            Panel.SetZIndex(tab, 10);   // يعلو جيرانه أثناء العبور
        }

        _dragOffset = dx;
        SetTabTranslate(tab, _dragOffset, animate: false);
        UpdateDropTarget(now);
        e.Handled = true;
    }

    private void TabHeader_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragTab is null) return;
        if (!_dragging) { _dragTab = null; return; }

        var tab = _dragTab;
        int target = _dragTargetIndex;
        Point drop = TabStrip is { } s ? e.GetPosition(s) : new Point(double.NaN, double.NaN);
        FinishTabDrag();

        if (target >= 0 && target != TerminalTabs.Items.IndexOf(tab)) MoveTabTo(tab, target);

        // الانتماء يقرّره مكان الإفلات على الشاشة: داخل إطار مجموعة ⇒ انضمّ، خارج كلّ الأطر ⇒ اخرج.
        // هذا ما يفعله المتصفّح، وهو الوحيد الذي يطابق ما يراه المستخدم لحظة الإفلات.
        ApplyDropGrouping(tab, drop);

        RefreshTabVisuals();
        if (!_restoring) SaveSession();
        e.Handled = true;
    }

    /// <summary>
    /// يحسب الموضع الذي سيهبط فيه المسحوب، ويفتح فجوةً بصريّة بإزاحة الجيران — «تُفسح الأوراق مكاناً».
    /// </summary>
    private void UpdateDropTarget(Point pointer)
    {
        var tabs = VisibleTabs();
        if (_dragTab is null || tabs.Count == 0) return;

        int target = tabs.Count - 1;
        for (int i = 0; i < tabs.Count; i++)
        {
            var t = tabs[i];
            Point topLeft = t.TranslatePoint(new Point(0, 0), TerminalTabs);
            double centre = topLeft.X - CurrentTranslate(t) + t.ActualWidth / 2;
            if (pointer.X < centre) { target = i; break; }
        }

        int dragIndex = tabs.IndexOf(_dragTab);
        if (dragIndex < 0) return;

        double width = _dragTab.ActualWidth;
        for (int i = 0; i < tabs.Count; i++)
        {
            if (i == dragIndex) continue;
            double shift = 0;
            if (dragIndex < target && i > dragIndex && i <= target) shift = -width;
            else if (dragIndex > target && i >= target && i < dragIndex) shift = width;
            SetTabTranslate(tabs[i], shift, animate: true);
        }

        _dragTargetIndex = TerminalTabs.Items.IndexOf(tabs[target]);
    }

    /// <summary>يُنهي السحب: يعيد كلّ الإزاحات إلى الصفر بانسياب ويستعيد شفافيّة الرأس.</summary>
    private void FinishTabDrag()
    {
        foreach (var t in TerminalTabs.Items.OfType<TabItem>())
        {
            SetTabTranslate(t, 0, animate: true);
            Panel.SetZIndex(t, 0);
            t.Opacity = 1;
        }

        // التصفير قبل تحرير الالتقاط: التحرير يُطلق LostMouseCapture ⇒ CancelTabDrag ⇒ عودةٌ إلى
        // هنا. بالتصفير أوّلاً تقف العودة عند فحص null بدل أن تُكرّر العمل.
        var tab = _dragTab;
        _dragTab = null;
        _dragging = false;
        _dragOffset = 0;
        tab?.ReleaseMouseCapture();
    }

    private void CancelTabDrag()
    {
        if (_dragTab is null) return;
        FinishTabDrag();
        _dragTargetIndex = -1;
    }

    /// <summary>التبويبات الظاهرة في الشريط بترتيبها (أعضاء المجموعات المطويّة مستبعدون).</summary>
    private List<TabItem> VisibleTabs() =>
        TerminalTabs.Items.OfType<TabItem>().Where(t => t.Visibility == Visibility.Visible).ToList();

    private static double CurrentTranslate(TabItem tab) =>
        tab.RenderTransform is TranslateTransform tt ? tt.X : 0;

    /// <summary>يزيح رأس تبويب أفقيّاً — بانسياب قصير أثناء فتح الفجوة، وفوريّاً تحت الإصبع.</summary>
    private static void SetTabTranslate(TabItem tab, double x, bool animate)
    {
        if (tab.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform();
            tab.RenderTransform = tt;
        }

        if (!animate)
        {
            tt.BeginAnimation(TranslateTransform.XProperty, null);
            tt.X = x;
            return;
        }

        if (Math.Abs(tt.X - x) < 0.5) return;
        tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>
    /// يقرّر انتماء التبويب من <b>مكان إفلاته</b>: داخل إطار مجموعة ⇒ انضمّ إليها، وخارج كلّ
    /// الأطر ⇒ اخرج من مجموعته. هذا سلوك المتصفّح، وهو الوحيد الذي يطابق ما يراه المستخدم.
    ///
    /// <para>الإطار يُرسَم قبل الإفلات بلحظة، فالقياس على حدوده الأخيرة صحيح. وحين لا تُعرَف نقطة
    /// الإفلات (سحب أُلغي) نُبقي الانتماء كما هو بدل تخمينه.</para>
    /// </summary>
    private void ApplyDropGrouping(TabItem tab, Point drop)
    {
        if (double.IsNaN(drop.X) || TabStrip is not { } strip) return;

        string? frame = strip.GroupFrameAt(drop);
        string mine = GetGroup(tab);

        if (frame is not null)
        {
            if (!string.Equals(frame, mine, StringComparison.OrdinalIgnoreCase)) AssignTabToGroup(tab, frame);
            return;
        }

        if (mine.Length == 0) return;
        SetGroup(tab, "");
        PruneGroups();
    }
    // ===== بنود القائمة السياقيّة =====

    /// <summary>يضيف قسم المجموعات إلى قائمة التبويب السياقيّة (يُستدعى من <c>BuildTabContextMenu</c>).</summary>
    private void AddGroupMenu(ContextMenu menu, TabItem tab)
    {
        var root = new MenuItem { Header = Loc.T("tabgrp.menu") };

        // عنصر نائب: قائمة فرعيّة بلا أبناء لا تُفتح أصلاً في WPF (HasItems=false ⇒ لا سهم ولا
        // حدث SubmenuOpened)، فيبدو البند ميّتاً. النائب يُستبدَل بالمحتوى الحقيقيّ عند أوّل فتح.
        root.Items.Add(new MenuItem { Header = "…", IsEnabled = false });

        root.SubmenuOpened += (_, _) =>
        {
            root.Items.Clear();
            string mine = GetGroup(tab);

            var create = new MenuItem { Header = Loc.T("tabgrp.new") };
            create.Click += (_, _) =>
            {
                string? name = Views.AppDialog.Prompt(this, Loc.T("tabgrp.new"), Loc.T("tabgrp.namePrompt"),
                    string.Format(Loc.T("tabgrp.defaultName"), ActiveGroups().Count + 1), Loc.T("dlg.save"));
                if (!string.IsNullOrWhiteSpace(name)) AssignTabToGroup(tab, name!);
            };
            root.Items.Add(create);

            var others = ActiveGroups().Where(g => !string.Equals(g, mine, StringComparison.OrdinalIgnoreCase)).ToList();
            if (others.Count > 0)
            {
                root.Items.Add(new Separator());
                foreach (string g in others)
                {
                    string captured = g;
                    var item = new MenuItem { Header = captured };
                    item.Click += (_, _) => AssignTabToGroup(tab, captured);
                    root.Items.Add(item);
                }
            }

            if (mine.Length == 0) return;

            root.Items.Add(new Separator());

            var rename = new MenuItem { Header = Loc.T("tabgrp.rename") };
            rename.Click += (_, _) =>
            {
                string? name = Views.AppDialog.Prompt(this, Loc.T("tabgrp.rename"), Loc.T("tabgrp.namePrompt"), mine, Loc.T("dlg.save"));
                if (!string.IsNullOrWhiteSpace(name)) RenameGroup(mine, name!);
            };
            root.Items.Add(rename);

            var collapse = new MenuItem
            {
                Header = Loc.T(_groups.TryGetValue(mine, out var g2) && g2.Collapsed ? "tabgrp.expand" : "tabgrp.collapse"),
            };
            collapse.Click += (_, _) => ToggleGroupCollapsed(mine);
            root.Items.Add(collapse);

            var leave = new MenuItem { Header = Loc.T("tabgrp.leave") };
            leave.Click += (_, _) => AssignTabToGroup(tab, "");
            root.Items.Add(leave);

            root.Items.Add(new Separator());
            root.Items.Add(BuildColorRow(menu, hex => SetGroupColor(mine, hex)));
        };

        menu.Items.Add(root);
    }

    // ===== الحفظ والاستعادة =====

    /// <summary>لون مجموعة التبويب (فارغ = بلا مجموعة أو بلا لون) — للقطة الجلسة.</summary>
    private string GroupColorOf(TabItem tab)
    {
        string g = GetGroup(tab);
        return g.Length > 0 && _groups.TryGetValue(g, out var grp) ? grp.Color : "";
    }

    /// <summary>هل مجموعة التبويب مطويّة؟ — للقطة الجلسة.</summary>
    private bool GroupCollapsedOf(TabItem tab)
    {
        string g = GetGroup(tab);
        return g.Length > 0 && _groups.TryGetValue(g, out var grp) && grp.Collapsed;
    }

    /// <summary>يعيد بناء مجموعة من لقطة مستعادة (اسم + لون + طيّ).</summary>
    private void RestoreGroup(TabItem tab, string? name, string? color, bool collapsed)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        string key = name!.Trim();
        SetGroup(tab, key);
        if (!_groups.TryGetValue(key, out var g)) _groups[key] = g = new TabGroup();
        if (!string.IsNullOrEmpty(color)) g.Color = color!;
        g.Collapsed = collapsed;
    }
}
