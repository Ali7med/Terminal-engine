using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Terminal.Servers.Models;
using Terminal.Servers.Parsing;
using Terminal.Servers.Scan;
using Terminal.Servers.Ssh;
using TerminalLauncher.Models;
using TerminalLauncher.Services;

namespace TerminalLauncher.Views;

/// <summary>
/// نافذة «مراقب الخوادم»: إدارة بروفايلات الخوادم + الاتّصال عبر SSH + عرض تخزينها (df) — الموجة 1.
/// المنطق النقيّ (SSH/المحلّلات) في مشروع <c>Terminal.Servers</c>؛ هذه النافذة قشرة UI فوقه.
/// </summary>
public partial class ServerMonitorWindow : Window
{
    private static readonly string[] PresetColors =
        { "#3B82F6", "#22C55E", "#EF4444", "#F59E0B", "#A855F7", "#14B8A6", "#EC4899", "#64748B" };

    private readonly ServerProfileService _service;
    private List<ServerProfile> _all = new();

    private ServerProfile? _editing;      // البروفايل قيد التحرير (null = جديد)
    private string _editColor = PresetColors[0];

    private ISshConnection? _connection;  // الاتّصال الحيّ (null = غير متّصل)
    private CancellationTokenSource? _cts;
    private ServerProfile? _connectedProfile;   // بروفايل الجلسة الحيّة (لبناء SFTP)

    private List<FileRowVm> _files = new();      // نتيجة «أكبر الملفّات» كاملةً (قبل تصفية البحث)
    private FileRowVm? _renameTarget;
    private Func<string, string, Task>? _renameRefresh;   // (newPath, newName) → تحديث المصدر بعد النجاح
    private FolderNode? _detailFolder;           // المجلّد المعروضة ملفّاته في لوحة التفاصيل
    private int _logSearchFrom;                  // موضع بدء البحث التالي في عارض السجلّ

    // حجم خطّ التفاصيل الموحّد (الشجرة + تفاصيل المجلّد + جدول الملفّات) — يُحفَظ ويبقى بين التشغيلات.
    private const string DetailFontKey = "server_detail_font_size";
    private const double MinDetailFont = 11, MaxDetailFont = 24;
    private readonly SettingsStore _settings = new();
    private double _detailFont = 14;

    public ServerMonitorWindow(ServerProfileService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        InitializeComponent();

        FlowDirection = Loc.Flow;
        Loc.Changed += ApplyTexts;
        Closed += (_, _) => { Loc.Changed -= ApplyTexts; Disconnect(); };

        BuildColorSwatches();
        BuildFavorites();
        FolderTree.PreviewMouseRightButtonDown += Tree_PreviewRightDown;
        DashTree.PreviewMouseRightButtonDown += Tree_PreviewRightDown;
        LoadDetailFont();
        ApplyDetailFont();
        ApplyTexts();
        _all = _service.LoadAll();
        RefreshList();
    }

    // ===== حجم خطّ التفاصيل الموحّد =====
    private void LoadDetailFont()
    {
        if (double.TryParse(_settings.GetRaw(DetailFontKey), System.Globalization.NumberStyles.Float,
                CultureInfo.InvariantCulture, out double v) && v >= MinDetailFont && v <= MaxDetailFont)
            _detailFont = v;
    }

    private void ApplyDetailFont()
    {
        FolderTree.FontSize = _detailFont;
        DashTree.FontSize = _detailFont;
        FolderFilesList.FontSize = _detailFont;
        FilesGrid.FontSize = _detailFont;
        DisksList.FontSize = _detailFont;
        FolderDetailHeader.FontSize = _detailFont;
        FolderDetailHint.FontSize = _detailFont;
        FontSizeLabel.Text = ((int)_detailFont).ToString(CultureInfo.InvariantCulture);
    }

    private void FontMinus_Click(object sender, RoutedEventArgs e) => ChangeDetailFont(-1);
    private void FontPlus_Click(object sender, RoutedEventArgs e) => ChangeDetailFont(+1);

    private void ChangeDetailFont(double delta)
    {
        double v = Math.Clamp(_detailFont + delta, MinDetailFont, MaxDetailFont);
        if (Math.Abs(v - _detailFont) < 0.01) return;
        _detailFont = v;
        ApplyDetailFont();
        _settings.SetRaw(DetailFontKey, _detailFont.ToString(CultureInfo.InvariantCulture));
    }

    // ===== الترجمة =====
    private void ApplyTexts()
    {
        FlowDirection = Loc.Flow;
        Title = Loc.T("srv.title");
        TitleText.Text = Loc.T("srv.title");
        ServersHeader.Text = Loc.T("srv.servers");
        AddBtn.ToolTip = Loc.T("srv.add");
        SearchBox.Tag = Loc.T("srv.search");
        ConnectBtn.Content = Loc.T("srv.connect");
        TestBtn.Content = Loc.T("srv.test");
        RefreshBtnText.Text = Loc.T("srv.refresh");
        DisconnectBtnText.Text = Loc.T("srv.disconnect");
        RailMenuBtn.ToolTip = Loc.T("srv.servers");
        RailAddBtn.ToolTip = Loc.T("srv.add");
        CtxEdit.Header = Loc.T("srv.edit");
        CtxDuplicate.Header = Loc.T("srv.duplicate");
        CtxDelete.Header = Loc.T("srv.delete");
        EmptyHintText.Text = _all.Count == 0 ? Loc.T("srv.noServers") : Loc.T("srv.pickServer");
        DisksLabel.Text = Loc.T("srv.disks");
        ModeDisks.Content = Loc.T("srv.tab.disks");
        ModeFolders.Content = Loc.T("srv.tab.folders");
        ScanBtn.Content = Loc.T("srv.folder.scan");
        CopyPathBtn.ToolTip = Loc.T("srv.folder.copyPath");
        FavoritesLabel.Text = Loc.T("srv.folder.favorites");
        PathBox.Tag = Loc.T("srv.folder.pathHint");

        // الملفّات + العارض
        ModeFiles.Content = Loc.T("srv.tab.files");
        ModeMgmt.Content = Loc.T("srv.tab.mgmt");
        MgmtProcLabel.Text = Loc.T("srv.mgmt.processes");
        MgmtSvcLabel.Text = Loc.T("srv.mgmt.services");
        MgmtPortLabel.Text = Loc.T("srv.mgmt.ports");
        MgmtSvcSearch.Tag = Loc.T("srv.files.search");
        ProcKill.Header = Loc.T("srv.mgmt.kill");
        ProcKill9.Header = Loc.T("srv.mgmt.kill9");
        SvcStart.Header = Loc.T("srv.mgmt.start");
        SvcStop.Header = Loc.T("srv.mgmt.stop");
        SvcRestart.Header = Loc.T("srv.mgmt.restart");
        MgmtProcMenu.FlowDirection = Loc.Flow;
        MgmtSvcMenu.FlowDirection = Loc.Flow;
        ModeDashboard.Content = Loc.T("srv.tab.dashboard");
        DashOverviewLabel.Text = Loc.T("srv.dash.overview");
        DashProcLabel.Text = Loc.T("srv.dash.topProc");
        DashTreemapLabel.Text = Loc.T("srv.dash.treemap");
        DashLargestLabel.Text = Loc.T("srv.dash.largest");
        DashLargestLoadBtn.Content = Loc.T("srv.dash.loadLargest");
        LiveToggle.Content = Loc.T("srv.live.toggle");
        DashTreeLabel.Text = Loc.T("srv.dash.tree");
        TileUptimeCap.Text = "⏱ " + Loc.T("srv.dash.uptime");
        TileLoadCap.Text = "⚙ " + Loc.T("srv.dash.load");
        TileRamCap.Text = "🧠 " + Loc.T("srv.dash.ram");
        TileRootCap.Text = "💽 " + Loc.T("srv.dash.rootDisk");
        OvHostLbl.Text = Loc.T("srv.dash.host");
        OvOsLbl.Text = Loc.T("srv.dash.os");
        OvKernelLbl.Text = Loc.T("srv.dash.kernel");
        OvCpuLbl.Text = Loc.T("srv.dash.cpu");
        OvIpLbl.Text = Loc.T("srv.dash.ip");
        ScanFilesBtn.Content = Loc.T("srv.files.scan");
        ExportBtn.ToolTip = Loc.T("srv.files.export");
        FilesPathBox.Tag = Loc.T("srv.folder.pathHint");
        FilesSearch.Tag = Loc.T("srv.files.search");
        ColName.Header = Loc.T("srv.col.name");
        ColExt.Header = Loc.T("srv.col.ext");
        ColSize.Header = Loc.T("srv.col.size");
        ColModified.Header = Loc.T("srv.col.modified");
        ColPath.Header = Loc.T("srv.col.path");
        FileDownload.Header = Loc.T("srv.file.download");
        FileViewLog.Header = Loc.T("srv.file.viewLog");
        FileRename.Header = Loc.T("srv.file.rename");
        FileCopyPath.Header = Loc.T("srv.file.copyPath");
        FileWhichContainer.Header = Loc.T("srv.file.whichContainer");
        FileDelete.Header = Loc.T("srv.file.delete");

        // لوحة «اعرف الحاوية»
        ContainerHeaderLabel.Text = Loc.T("srv.docker.ownerTitle");
        ContainerLoadingText.Text = Loc.T("srv.docker.resolving");
        ContainerCopyName.Content = Loc.T("srv.docker.copyName");
        ContainerCopyId.Content = Loc.T("srv.docker.copyId");
        RenameTitle.Text = Loc.T("srv.file.rename");
        RenameCancel.Content = Loc.T("srv.ed.cancel");
        RenameSave.Content = Loc.T("srv.ed.save");
        LogSearch.Tag = Loc.T("srv.log.search");

        // تفاصيل المجلّد + التحكّم بالخطّ
        DetailDownload.Header = Loc.T("srv.file.download");
        DetailView.Header = Loc.T("srv.file.viewLog");
        DetailRename.Header = Loc.T("srv.file.rename");
        DetailCopy.Header = Loc.T("srv.file.copyPath");
        DetailWhichContainer.Header = Loc.T("srv.file.whichContainer");
        DetailDelete.Header = Loc.T("srv.file.delete");
        FolderDetailLoadingText.Text = Loc.T("srv.file.loading");
        FilesLoadingText.Text = Loc.T("srv.file.loading");

        // اتّجاه القوائم السياقيّة يتبع لغة الواجهة (فتظهر الأيقونات في الجهة الصحيحة)
        ServerListMenu.FlowDirection = Loc.Flow;
        DetailMenu.FlowDirection = Loc.Flow;
        FilesMenu.FlowDirection = Loc.Flow;
        BuildTreeMenus();   // تُبنى القائمة السياقيّة للشجرتين بلغة/اتّجاه الواجهة الحاليّين
        FontMinusBtn.ToolTip = Loc.T("srv.font.smaller");
        FontPlusBtn.ToolTip = Loc.T("srv.font.larger");
        if (FolderTree.Items.Count == 0)
            FolderDetailHint.Text = Loc.T("srv.folder.detailHint");

        LblName.Text = Loc.T("srv.ed.name");
        LblHost.Text = Loc.T("srv.ed.host");
        LblPort.Text = Loc.T("srv.ed.port");
        LblUser.Text = Loc.T("srv.ed.user");
        LblAuth.Text = Loc.T("srv.ed.auth");
        LblColor.Text = Loc.T("srv.ed.color");
        LblNotes.Text = Loc.T("srv.ed.notes");
        LblKeyPass.Text = Loc.T("srv.ed.keyPass");
        EditorCancel.Content = Loc.T("srv.ed.cancel");
        EditorSave.Content = Loc.T("srv.ed.save");

        if (_connection is null) SetStatusIdle();
    }

    // ===== قائمة الخوادم =====
    private void RefreshList()
    {
        string q = SearchBox.Text?.Trim() ?? "";
        IEnumerable<ServerProfile> view = _all;
        if (q.Length > 0)
            view = _all.Where(p =>
                p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Host.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Username.Contains(q, StringComparison.OrdinalIgnoreCase));

        var selected = ServerList.SelectedItem as ServerProfile;
        ServerList.ItemsSource = view.ToList();
        ServerDots.ItemsSource = _all;   // الشريط المطويّ يعرض كلّ الخوادم (بلا تصفية بحث)
        if (selected != null)
            ServerList.SelectedItem = _all.FirstOrDefault(p => p.Id == selected.Id);

        EmptyHintText.Text = _all.Count == 0 ? Loc.T("srv.noServers") : Loc.T("srv.pickServer");
    }

    // ===== الشريط المطويّ + اللوحة المنبثقة =====
    private void ServerRail_MouseEnter(object sender, MouseEventArgs e) => ShowFlyout(true);
    private void ServerFlyout_MouseLeave(object sender, MouseEventArgs e) => ShowFlyout(false);
    private void RailMenu_Click(object sender, RoutedEventArgs e) => ShowFlyout(ServerFlyout.Visibility != Visibility.Visible);

    private readonly TranslateTransform _flyoutTransform = new();

    /// <summary>يفتح/يغلق لوحة الخوادم بانزلاق + تلاشٍ خفيف (≈150مي — لا بطيء ولا سريع جدّاً).</summary>
    private void ShowFlyout(bool show)
    {
        ServerFlyout.RenderTransform = _flyoutTransform;
        if (show)
        {
            if (ServerFlyout.Visibility == Visibility.Visible) return;
            ServerFlyout.Visibility = Visibility.Visible;
            var dur = TimeSpan.FromMilliseconds(150);
            ServerFlyout.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
            _flyoutTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-14, 0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            if (ServerFlyout.Visibility != Visibility.Visible) return;
            var dur = TimeSpan.FromMilliseconds(130);
            var fade = new DoubleAnimation(1, 0, dur);
            fade.Completed += (_, _) => { if (ServerFlyout.Opacity == 0) ServerFlyout.Visibility = Visibility.Collapsed; };
            ServerFlyout.BeginAnimation(OpacityProperty, fade);
            _flyoutTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, -14, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        }
    }

    private void RailDot_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ServerProfile p) return;
        ServerList.SelectedItem = _all.FirstOrDefault(x => x.Id == p.Id);
        ShowFlyout(false);
        _ = ConnectAsync(p);
    }

    private ServerProfile? Selected => ServerList.SelectedItem as ServerProfile;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool has = Selected != null;
        ConnectBtn.IsEnabled = has;
        TestBtn.IsEnabled = has;
    }

    private void ServerList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected != null) _ = ConnectAsync(Selected);
    }

    // ===== الإضافة/التعديل/النسخ/الحذف =====
    private void AddBtn_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void EditBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Selected != null) OpenEditor(Selected);
    }

    private void DuplicateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        var copy = p.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = p.Name + " (copy)";
        copy.LastConnected = null;
        _service.Save(copy);
        _all = _service.LoadAll();
        RefreshList();
        ServerList.SelectedItem = _all.FirstOrDefault(x => x.Id == copy.Id);
    }

    private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        if (!await ConfirmAsync(Loc.T("srv.delete"), Loc.T("srv.ed.deleteConfirm"), p.DisplaySubtitle).ConfigureAwait(true))
            return;
        _service.Delete(p.Id);
        _all = _service.LoadAll();
        RefreshList();
    }

    private void OpenEditor(ServerProfile? profile)
    {
        ShowFlyout(false);
        _editing = profile;
        bool isNew = profile is null;
        EditorTitle.Text = Loc.T(isNew ? "srv.ed.addTitle" : "srv.ed.editTitle");

        // قائمة المصادقة
        EdAuth.Items.Clear();
        EdAuth.Items.Add(Loc.T("srv.ed.authPassword"));
        EdAuth.Items.Add(Loc.T("srv.ed.authKey"));

        EdName.Text = profile?.Name ?? "";
        EdHost.Text = profile?.Host ?? "";
        EdPort.Text = (profile?.Port ?? 22).ToString(CultureInfo.InvariantCulture);
        EdUser.Text = profile?.Username ?? "";
        EdAuth.SelectedIndex = profile?.AuthKind == SshAuthKind.PrivateKey ? 1 : 0;
        EdPassword.Password = "";
        EdKey.Text = "";
        EdKeyPass.Password = "";
        EdNotes.Text = profile?.Notes ?? "";
        _editColor = profile?.EffectiveColor ?? PresetColors[0];

        SecretHint.Text = profile?.HasStoredSecret == true ? Loc.T("srv.ed.secretKept") : "";
        UpdateAuthVisibility();
        UpdateColorSelection();

        EditorOverlay.Visibility = Visibility.Visible;
        EdName.Focus();
    }

    private void EdAuth_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAuthVisibility();

    private void UpdateAuthVisibility()
    {
        bool key = EdAuth.SelectedIndex == 1;
        LblSecret.Text = Loc.T(key ? "srv.ed.key" : "srv.ed.password");
        EdPassword.Visibility = key ? Visibility.Collapsed : Visibility.Visible;
        EdKey.Visibility = key ? Visibility.Visible : Visibility.Collapsed;
        LblKeyPass.Visibility = key ? Visibility.Visible : Visibility.Collapsed;
        EdKeyPass.Visibility = key ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildColorSwatches()
    {
        ColorPanel.Children.Clear();
        foreach (var hex in PresetColors)
        {
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                Background = ParseBrush(hex),
                Tag = hex,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
            };
            swatch.MouseLeftButtonDown += (_, _) => { _editColor = hex; UpdateColorSelection(); };
            ColorPanel.Children.Add(swatch);
        }
    }

    private void UpdateColorSelection()
    {
        foreach (var child in ColorPanel.Children)
            if (child is Border b)
                b.BorderBrush = (string)b.Tag == _editColor
                    ? (Brush)FindResource("Brush.Text") : Brushes.Transparent;
    }

    private void EditorCancel_Click(object sender, RoutedEventArgs e) => EditorOverlay.Visibility = Visibility.Collapsed;
    private void EditorOverlay_MouseDown(object sender, MouseButtonEventArgs e) => EditorOverlay.Visibility = Visibility.Collapsed;
    private void EditorCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void EditorSave_Click(object sender, RoutedEventArgs e)
    {
        string name = EdName.Text.Trim();
        string host = EdHost.Text.Trim();
        if (name.Length == 0 || host.Length == 0) { EdName.Focus(); return; }

        var p = _editing ?? new ServerProfile();
        p.Name = name;
        p.Host = host;
        p.Port = int.TryParse(EdPort.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port > 0 ? port : 22;
        p.Username = EdUser.Text.Trim();
        p.AuthKind = EdAuth.SelectedIndex == 1 ? SshAuthKind.PrivateKey : SshAuthKind.Password;
        p.Color = _editColor;
        p.Notes = string.IsNullOrWhiteSpace(EdNotes.Text) ? null : EdNotes.Text.Trim();

        // السرّ الخام: يُضبَط فقط إن أُدخل (وإلّا يُبقى المخزَّن)
        string rawSecret = p.AuthKind == SshAuthKind.PrivateKey ? EdKey.Text : EdPassword.Password;
        if (!string.IsNullOrEmpty(rawSecret)) p.Secret = rawSecret;
        if (p.AuthKind == SshAuthKind.PrivateKey && EdKeyPass.Password.Length > 0)
            p.KeyPassphrase = EdKeyPass.Password;

        _service.Save(p);
        _all = _service.LoadAll();
        RefreshList();
        ServerList.SelectedItem = _all.FirstOrDefault(x => x.Id == p.Id);
        EditorOverlay.Visibility = Visibility.Collapsed;
    }

    // ===== الاتّصال / الفحص =====
    private void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Selected != null) _ = ConnectAsync(Selected);
    }

    private void DisconnectBtn_Click(object sender, RoutedEventArgs e) => Disconnect();

    private void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_connection?.IsConnected != true) return;
        if (ModeDashboard.IsChecked == true) { _dashLoaded = false; _ = LoadDashboardAsync(); }
        else if (ModeDisks.IsChecked == true) _ = ScanDisksAsync();
        else if (ModeFolders.IsChecked == true) _ = ScanPathAsync(PathBox.Text?.Trim() ?? "/");
        else if (ModeFiles.IsChecked == true) _ = ScanFilesAsync(FilesPathBox.Text?.Trim() ?? "/");
    }

    private async Task ConnectAsync(ServerProfile profile)
    {
        Disconnect();
        SetStatus(Loc.T("srv.status.connecting"), profile.DisplaySubtitle, "connecting");
        ConnectBtn.IsEnabled = false;
        TestBtn.IsEnabled = false;

        _cts = new CancellationTokenSource();
        try
        {
            var info = _service.BuildConnectionInfo(profile);
            var conn = new SshNetConnection(info);
            // عند سقوط الجلسة الخاملة تُعيد الطبقة الاتّصال تلقائيّاً قبل تنفيذ الأمر — نُظهر توستاً لحظتها.
            conn.OnReconnecting = () => Dispatcher.BeginInvoke(() =>
            {
                if (_connection == conn) ShowToast(Loc.T("srv.status.reconnecting"));
            });
            await conn.ConnectAsync(_cts.Token).ConfigureAwait(true);
            _connection = conn;
            _connectedProfile = profile;
            _service.MarkConnected(profile);
            RefreshList();

            SetStatus(Loc.T("srv.status.connected"), profile.DisplaySubtitle, "connected");
            DisconnectBtn.IsEnabled = true;
            RefreshBtn.IsEnabled = true;
            ResetFolders();
            ResetFiles();
            ResetDashboard();
            ModeDashboard.IsChecked = true;   // لوحة القيادة هي الافتراضيّة عند الاتّصال
            SetConnectedUi(true);              // → ApplyMode → LoadDashboardAsync
            ShowFlyout(false);
        }
        catch (Exception ex)
        {
            _connection = null;
            SetStatus(Loc.T("srv.status.failed"), ex.Message, "failed");
        }
        finally
        {
            ConnectBtn.IsEnabled = Selected != null;
            TestBtn.IsEnabled = Selected != null;
        }
    }

    private async Task ScanDisksAsync()
    {
        if (_connection is null) return;
        try
        {
            var scanner = new StorageScanner(_connection);
            var disks = await scanner.QuickScanDisksAsync(_cts?.Token ?? default).ConfigureAwait(true);
            DisksList.ItemsSource = disks.Select(DiskRowVm.From).ToList();
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("srv.status.failed"), ex.Message, "failed");
        }
    }

    // ===== حوار تأكيد مُنسّق (بديل MessageBox) =====
    private TaskCompletionSource<bool>? _confirmTcs;

    /// <summary>يعرض حوار تأكيد ويُعيد true إن أكّد المستخدم. <paramref name="path"/> يُبرَز LTR.</summary>
    private Task<bool> ConfirmAsync(string title, string message, string path)
    {
        ConfirmTitle.Text = title;
        ConfirmMessage.Text = message;
        ConfirmPath.Text = path;
        _confirmTcs?.TrySetResult(false);   // أنهِ أيّ حوار سابق
        _confirmTcs = new TaskCompletionSource<bool>();
        ConfirmOverlay.Visibility = Visibility.Visible;
        ConfirmOk.Focus();
        return _confirmTcs.Task;
    }

    private void ConfirmOk_Click(object sender, RoutedEventArgs e) => CloseConfirm(true);
    private void ConfirmCancel_Click(object sender, RoutedEventArgs e) => CloseConfirm(false);
    private void ConfirmOverlay_MouseDown(object sender, MouseButtonEventArgs e) => CloseConfirm(false);

    private void CloseConfirm(bool result)
    {
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        var tcs = _confirmTcs;
        _confirmTcs = null;
        tcs?.TrySetResult(result);
    }

    // ===== إجراءات القرص: الدخول لنقطة التركيب =====
    private static DiskRowVm? DiskOf(object sender) => (sender as FrameworkElement)?.DataContext as DiskRowVm;

    private void DiskCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && DiskOf(sender) is { } d) NavigateToFolders(d.Mount);
    }
    private void DiskOpenFolders_Click(object sender, RoutedEventArgs e)
    {
        if (DiskOf(sender) is { } d) NavigateToFolders(d.Mount);
    }
    private void DiskOpenFiles_Click(object sender, RoutedEventArgs e)
    {
        if (DiskOf(sender) is { } d) NavigateToFiles(d.Mount);
    }
    private void DiskCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (DiskOf(sender) is { } d) CopyPathWithToast(d.Mount);
    }
    private void DiskCard_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((sender as FrameworkElement)?.ContextMenu is { } m) m.FlowDirection = Loc.Flow;
    }

    // ===== لوحة القيادة + المراقبة الحيّة =====
    private bool _dashLoaded;
    private int _cpuCores;
    private System.Windows.Threading.DispatcherTimer? _liveTimer;
    private readonly HashSet<string> _activeAlerts = new();   // تنبيهات نشطة (لمنع التكرار كلّ تحديث)

    private void ResetDashboard()
    {
        _dashLoaded = false;
        DashTree.Items.Clear();
        DashTreemap.ItemsSource = null;
        DashLargest.ItemsSource = null;
        DashProcesses.ItemsSource = null;
        TileUptime.Text = TileLoad.Text = TileRam.Text = TileRoot.Text = "—";
        TileRamBar.Value = 0;
        TileRootBar.Value = 0;
        OvHost.Text = OvOs.Text = OvKernel.Text = OvCpu.Text = OvIp.Text = "—";
        DashTreemapHint.Visibility = DashLargestHint.Visibility = Visibility.Collapsed;
        DashTreemapLoading.Visibility = DashTreeLoading.Visibility = Visibility.Visible;
        DashLargestLoading.Visibility = Visibility.Collapsed;   // «أكبر الملفّات» كسول: زرّ بدل تحميل تلقائيّ
        DashLargestLoadBtn.Visibility = Visibility.Visible;
    }

    private Task LoadDashboardAsync()
    {
        if (_connection is null || _dashLoaded) return Task.CompletedTask;
        _dashLoaded = true;
        DashTreemapLoading.Visibility = DashTreeLoading.Visibility = Visibility.Visible;
        DashTreemapHint.Visibility = DashLargestHint.Visibility = Visibility.Collapsed;
        DashLargest.ItemsSource = null;                         // مسح نتائج أكبر الملفّات السابقة
        DashLargestLoading.Visibility = Visibility.Collapsed;
        DashLargestLoadBtn.Visibility = Visibility.Visible;     // يعود كسولاً عند كلّ تحميل/تحديث
        // نطلق الخفيف والثقيل بالتوازي كي لا يؤخّر فحصُ التخزين البطيء عرضَ معلومات النظام/الأداء.
        _ = LoadDashLightAsync();
        _ = LoadDashStorageAsync();
        return Task.CompletedTask;
    }

    /// <summary>
    /// معلومات النظام + الأداء + الأقراص في **نداء SSH واحد** (بدل ثلاث جولات متتابعة) — تسريعٌ ملموس
    /// خصوصاً على الاتّصالات ذات الكمون العالي. تُقسَّم المخرجات بعلامتَي <c>===PERF===</c>/<c>===DF===</c>.
    /// </summary>
    private async Task LoadDashLightAsync()
    {
        var ct = _cts?.Token ?? default;
        try
        {
            string cmd = SystemInfoScanner.SnapshotCommand()
                + "; echo ===PERF===; " + PerfMonitor.SnapshotCommand(8)
                + "; echo ===DF===; df -kP";
            var r = await _connection!.RunAsync(cmd, ct).ConfigureAwait(true);

            string text = (r.StdOut ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            int iPerf = text.IndexOf("===PERF===", StringComparison.Ordinal);
            int iDf = text.IndexOf("===DF===", StringComparison.Ordinal);
            string sysPart = iPerf >= 0 ? text[..iPerf] : text;
            string perfPart = iPerf >= 0 && iDf > iPerf ? text[(iPerf + 10)..iDf] : "";
            string dfPart = iDf >= 0 ? text[(iDf + 8)..] : "";

            var sys = SystemInfoScanner.Parse(sysPart);
            OvHost.Text = Dash(sys.Hostname);
            OvOs.Text = Dash(sys.OsName);
            OvKernel.Text = Dash(sys.Kernel);
            OvCpu.Text = sys.CpuCores > 0 ? $"{Dash(sys.CpuModel)} · {sys.CpuCores} " + Loc.T("srv.dash.cores") : Dash(sys.CpuModel);
            OvIp.Text = Dash(sys.Ip);
            _cpuCores = sys.CpuCores;

            var perf = PerfMonitor.ParseSnapshot(perfPart);
            TileUptime.Text = string.IsNullOrWhiteSpace(perf.Uptime) ? "—" : perf.Uptime;
            TileLoad.Text = perf.LoadAvg1.ToString("0.00", CultureInfo.InvariantCulture);
            TileRam.Text = $"{DiskRowVm.Human(perf.MemUsedKb * 1024)} / {DiskRowVm.Human(perf.MemTotalKb * 1024)} · {perf.MemUsedPercent:0}%";
            TileRamBar.Value = perf.MemUsedPercent;
            DashProcesses.ItemsSource = perf.TopProcesses.Select(ProcRowVm.From).ToList();

            var disks = OutputParsers.ParseDf(dfPart);
            var root = disks.FirstOrDefault(d => d.MountPoint == "/") ?? disks.FirstOrDefault();
            if (root != null)
            {
                TileRoot.Text = $"{DiskRowVm.Human(root.UsedBytes)} / {DiskRowVm.Human(root.TotalBytes)} · {root.UsePercent:0}%";
                TileRootBar.Value = root.UsePercent;
            }
            CheckAlerts(disks, perf);
        }
        catch { /* تُترك الحقول كما هي */ }
    }

    // ===== المراقبة الحيّة (تحديث دوريّ لبطاقات الأداء) =====
    private void LiveToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (LiveToggle.IsChecked == true) StartLive();
        else StopLive();
    }

    private void StartLive()
    {
        if (_connection?.IsConnected != true) return;
        if (_liveTimer?.IsEnabled == true) return;   // يعمل أصلاً
        _liveTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _liveTimer.Tick -= LiveTick;
        _liveTimer.Tick += LiveTick;
        _liveTimer.Start();
        _ = LivePerfTickAsync();   // تحديث فوريّ أوّل
    }

    private void StopLive()
    {
        _liveTimer?.Stop();
        LiveUpdated.Text = "";
    }

    private void LiveTick(object? sender, EventArgs e) => _ = LivePerfTickAsync();

    /// <summary>تحديث خفيف دوريّ: الأداء + الأقراص فقط (نداء واحد) — لا الأجزاء الثقيلة.</summary>
    private async Task LivePerfTickAsync()
    {
        if (_connection?.IsConnected != true) { StopLive(); return; }
        try
        {
            string cmd = PerfMonitor.SnapshotCommand(8) + "; echo ===DF===; df -kP";
            var r = await _connection.RunAsync(cmd, _cts?.Token ?? default).ConfigureAwait(true);
            string text = (r.StdOut ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            int iDf = text.IndexOf("===DF===", StringComparison.Ordinal);
            string perfPart = iDf >= 0 ? text[..iDf] : text;
            string dfPart = iDf >= 0 ? text[(iDf + 8)..] : "";

            var perf = PerfMonitor.ParseSnapshot(perfPart);
            TileUptime.Text = string.IsNullOrWhiteSpace(perf.Uptime) ? "—" : perf.Uptime;
            TileLoad.Text = perf.LoadAvg1.ToString("0.00", CultureInfo.InvariantCulture);
            TileRam.Text = $"{DiskRowVm.Human(perf.MemUsedKb * 1024)} / {DiskRowVm.Human(perf.MemTotalKb * 1024)} · {perf.MemUsedPercent:0}%";
            TileRamBar.Value = perf.MemUsedPercent;
            DashProcesses.ItemsSource = perf.TopProcesses.Select(ProcRowVm.From).ToList();

            var disks = OutputParsers.ParseDf(dfPart);
            var root = disks.FirstOrDefault(d => d.MountPoint == "/") ?? disks.FirstOrDefault();
            if (root != null)
            {
                TileRoot.Text = $"{DiskRowVm.Human(root.UsedBytes)} / {DiskRowVm.Human(root.TotalBytes)} · {root.UsePercent:0}%";
                TileRootBar.Value = root.UsePercent;
            }
            CheckAlerts(disks, perf);
            LiveUpdated.Text = $"{Loc.T("srv.live.updated")} {DateTime.Now:HH:mm:ss}";
        }
        catch { /* نتجاهل خطأ لقطة واحدة؛ التوقيت يُعيد المحاولة */ }
    }

    /// <summary>ينبّه عند تجاوز عتبات (قرص ≥90% أو حِمل &gt; عدد الأنوية) مرّةً عند الدخول للحالة فقط.</summary>
    private void CheckAlerts(IReadOnlyList<DiskInfo> disks, PerfSnapshot perf)
    {
        foreach (var d in disks)
        {
            string key = "disk:" + d.MountPoint;
            if (d.UsePercent >= 90)
            {
                if (_activeAlerts.Add(key))
                    NotificationService.Warning(Loc.T("srv.alert.diskFull"), $"{d.MountPoint} — {d.UsePercent:0}%");
            }
            else _activeAlerts.Remove(key);
        }

        if (_cpuCores > 0)
        {
            if (perf.LoadAvg1 > _cpuCores)
            {
                if (_activeAlerts.Add("load"))
                    NotificationService.Warning(Loc.T("srv.alert.highLoad"),
                        $"{perf.LoadAvg1.ToString("0.00", CultureInfo.InvariantCulture)} / {_cpuCores} {Loc.T("srv.dash.cores")}");
            }
            else _activeAlerts.Remove("load");
        }
    }

    /// <summary>Treemap المجلّدات + الشجرة (فحص <c>du -x</c> لـ <c>/</c>). «أكبر الملفّات» كسول بزرّ.</summary>
    private async Task LoadDashStorageAsync()
    {
        var ct = _cts?.Token ?? default;
        try
        {
            var scan = await new StorageScanner(_connection!).ScanSubfoldersAsync("/", ct).ConfigureAwait(true);
            long max = scan.Children.Count > 0 ? scan.Children[0].SizeBytes : 1;
            DashTreemap.ItemsSource = scan.Children.Take(8).Select((d, i) => TreemapRowVm.From(d, max, i)).ToList();
            ShowDashHint(DashTreemapHint, DashTreemapLoading, scan.Children.Count == 0 ? Loc.T("srv.folder.noFiles") : null);

            // الشجرة: الجذر + أبناؤه المباشرون (تحميل كسول لبقيّة الفروع)
            var rootNode = FolderNode.Dir(scan.Path, scan.TotalBytes);
            rootNode.Loaded = true;
            rootNode.Children.Clear();
            foreach (var c in scan.Children)
                rootNode.AddChild(FolderNode.Dir(c.Path, c.SizeBytes));
            DashTree.Items.Clear();
            DashTree.Items.Add(rootNode);
            DashTreeLoading.Visibility = Visibility.Collapsed;
            if (DashTree.ItemContainerGenerator.ContainerFromItem(rootNode) is TreeViewItem tvi) tvi.IsExpanded = true;
        }
        catch (Exception ex)
        {
            ShowDashHint(DashTreemapHint, DashTreemapLoading, ex.Message);
            DashTreeLoading.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>«أكبر الملفّات» تحت <c>/</c> — كسول (فحص <c>find</c> بطيء) يُطلَق بزرّ عند الحاجة.</summary>
    private void DashLargestLoadBtn_Click(object sender, RoutedEventArgs e) => _ = LoadDashLargestAsync();

    private async Task LoadDashLargestAsync()
    {
        if (_connection is null) return;
        DashLargestLoadBtn.Visibility = Visibility.Collapsed;
        DashLargestHint.Visibility = Visibility.Collapsed;
        DashLargestLoading.Visibility = Visibility.Visible;
        try
        {
            var largest = await new StorageScanner(_connection!)
                .LargestFilesAsync("/", 12, _cts?.Token ?? default).ConfigureAwait(true);
            DashLargest.ItemsSource = largest.Select(FileRowVm.From).ToList();
            ShowDashHint(DashLargestHint, DashLargestLoading, largest.Count == 0 ? Loc.T("srv.files.empty") : null);
        }
        catch (Exception ex)
        {
            ShowDashHint(DashLargestHint, DashLargestLoading, ex.Message);
            DashLargestLoadBtn.Visibility = Visibility.Visible;   // أتِح إعادة المحاولة
        }
    }

    /// <summary>يُخفي شريط التحميل ويُظهر رسالة (فراغ/خطأ) أو يخفيها عند وجود بيانات.</summary>
    private static void ShowDashHint(TextBlock hint, ProgressBar loading, string? text)
    {
        loading.Visibility = Visibility.Collapsed;
        if (text != null) { hint.Text = text; hint.Visibility = Visibility.Visible; }
        else hint.Visibility = Visibility.Collapsed;
    }

    private static string Dash(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s!;

    // ===== الإدارة: العمليّات + الخدمات + المنافذ =====
    private List<ServiceRowVm> _services = new();

    private Task LoadMgmtAsync()
    {
        _ = LoadProcessesAsync();
        _ = LoadServicesAsync();
        _ = LoadPortsAsync();
        return Task.CompletedTask;
    }

    private void ResetMgmt()
    {
        MgmtProcesses.ItemsSource = null;
        MgmtServices.ItemsSource = null;
        MgmtPorts.ItemsSource = null;
        _services = new List<ServiceRowVm>();
    }

    private void MgmtProcRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadProcessesAsync();
    private void MgmtSvcRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadServicesAsync();
    private void MgmtPortRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadPortsAsync();

    private async Task LoadProcessesAsync()
    {
        if (_connection is null) return;
        MgmtProcLoading.Visibility = Visibility.Visible;
        try
        {
            var procs = await new ServerAdmin(_connection).ListProcessesAsync(50, _cts?.Token ?? default).ConfigureAwait(true);
            MgmtProcesses.ItemsSource = procs.Select(MgmtProcVm.From).ToList();
        }
        catch (Exception ex) { NotificationService.Error(Loc.T("srv.mgmt.processes"), ex.Message); }
        finally { MgmtProcLoading.Visibility = Visibility.Collapsed; }
    }

    private async Task LoadServicesAsync()
    {
        if (_connection is null) return;
        MgmtSvcLoading.Visibility = Visibility.Visible;
        try
        {
            var svc = await new ServerAdmin(_connection).ListServicesAsync(_cts?.Token ?? default).ConfigureAwait(true);
            _services = svc.Select(ServiceRowVm.From).ToList();
            ApplyServiceFilter();
        }
        catch (Exception ex) { NotificationService.Error(Loc.T("srv.mgmt.services"), ex.Message); }
        finally { MgmtSvcLoading.Visibility = Visibility.Collapsed; }
    }

    private void MgmtSvcSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyServiceFilter();
    private void ApplyServiceFilter()
    {
        string q = MgmtSvcSearch.Text?.Trim() ?? "";
        IEnumerable<ServiceRowVm> view = _services;
        if (q.Length > 0)
            view = _services.Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                     || x.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
        MgmtServices.ItemsSource = view.ToList();
    }

    private async Task LoadPortsAsync()
    {
        if (_connection is null) return;
        MgmtPortLoading.Visibility = Visibility.Visible;
        try
        {
            var ports = await new ServerAdmin(_connection).ListPortsAsync(_cts?.Token ?? default).ConfigureAwait(true);
            MgmtPorts.ItemsSource = ports.OrderBy(p => p.Port)
                .Select(p => new PortRowVm { Port = p.Port, Address = p.Address, Process = p.Process }).ToList();
        }
        catch (Exception ex) { NotificationService.Error(Loc.T("srv.mgmt.ports"), ex.Message); }
        finally { MgmtPortLoading.Visibility = Visibility.Collapsed; }
    }

    // إنهاء العمليّات
    private void ProcKill_Click(object sender, RoutedEventArgs e) => _ = KillSelectedAsync(false);
    private void ProcKill9_Click(object sender, RoutedEventArgs e) => _ = KillSelectedAsync(true);
    private async Task KillSelectedAsync(bool force)
    {
        if (MgmtProcesses.SelectedItem is not MgmtProcVm p || _connection is null) return;
        string title = force ? Loc.T("srv.mgmt.kill9") : Loc.T("srv.mgmt.kill");
        if (!await ConfirmAsync(title, $"{Loc.T("srv.mgmt.killConfirm")} {p.Pid}", p.Command).ConfigureAwait(true)) return;
        try
        {
            await new ServerAdmin(_connection).KillAsync(p.Pid, force, _cts?.Token ?? default).ConfigureAwait(true);
            NotificationService.Secondary(Loc.T("srv.mgmt.killed"), NotificationType.Success);
            await LoadProcessesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { NotificationService.Error(title, ex.Message); }
    }

    // خدمات systemd
    private void SvcStart_Click(object sender, RoutedEventArgs e) => _ = ServiceActionAsync("start");
    private void SvcStop_Click(object sender, RoutedEventArgs e) => _ = ServiceActionAsync("stop");
    private void SvcRestart_Click(object sender, RoutedEventArgs e) => _ = ServiceActionAsync("restart");
    private async Task ServiceActionAsync(string action)
    {
        if (MgmtServices.SelectedItem is not ServiceRowVm svc || _connection is null) return;
        try
        {
            await new ServerAdmin(_connection).ServiceActionAsync(svc.Name, action, _cts?.Token ?? default).ConfigureAwait(true);
            NotificationService.Secondary($"{svc.Name} · {action}", NotificationType.Success);
            await LoadServicesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { NotificationService.Error(svc.Name, ex.Message); }
    }

    private async void TestBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        TestBtn.IsEnabled = false;
        try
        {
            var info = _service.BuildConnectionInfo(profile);
            using var conn = new SshNetConnection(info);
            await conn.ConnectAsync().ConfigureAwait(true);
            conn.Disconnect();
            NotificationService.Success(Loc.T("srv.test"), $"{profile.Name} — {Loc.T("srv.testOk")}");
        }
        catch (Exception ex)
        {
            NotificationService.Error(Loc.T("srv.test"), $"{Loc.T("srv.testFail")} {ex.Message}");
        }
        finally
        {
            TestBtn.IsEnabled = Selected != null;
        }
    }

    private void Disconnect()
    {
        StopLive();
        _activeAlerts.Clear();
        LiveToggle.IsChecked = false;
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _connection?.Dispose();
        _connection = null;
        _connectedProfile = null;
        DisconnectBtn.IsEnabled = false;
        RefreshBtn.IsEnabled = false;
        DisksList.ItemsSource = null;
        ResetFolders();
        ResetFiles();
        ResetDashboard();
        ResetMgmt();
        SetConnectedUi(false);
        SetStatusIdle();
    }

    /// <summary>يبدّل بين واجهة الاتّصال (شريط الوضع + اللوحات) وتلميح الفراغ.</summary>
    private void SetConnectedUi(bool connected)
    {
        ModeBar.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        if (connected) ApplyMode();
        else
        {
            DashboardScroller.Visibility = Visibility.Collapsed;
            ResultScroller.Visibility = Visibility.Collapsed;
            FoldersPanel.Visibility = Visibility.Collapsed;
            FilesPanel.Visibility = Visibility.Collapsed;
        }
    }

    // ===== شريط الحالة =====
    private void SetStatusIdle() => SetStatus(Loc.T("srv.status.idle"), "", "idle");

    private void SetStatus(string text, string sub, string kind)
    {
        StatusText.Text = text;
        StatusSub.Text = sub;
        StatusDot.Background = kind switch
        {
            "connected" => Brush("Brush.Accent"),
            "connecting" => ParseBrush("#F59E0B"),
            "failed" => Brush("Brush.Danger"),
            _ => Brush("Brush.TextMuted"),
        };
    }

    // ===== مستكشف المجلّدات (الموجة 2) =====
    private static readonly string[] FavoritePaths =
        { "/", "/var", "/var/log", "/var/www", "/home", "/etc", "/opt", "/tmp" };

    private void BuildFavorites()
    {
        FavoritesPanel.Children.Clear();
        foreach (var path in FavoritePaths)
        {
            var chip = new Button
            {
                Content = path,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 6),
                FontSize = 11.5,
                FlowDirection = FlowDirection.LeftToRight,
                Tag = path,
                Cursor = Cursors.Hand,
            };
            chip.Click += (_, _) => { PathBox.Text = path; _ = ScanPathAsync(path); };
            FavoritesPanel.Children.Add(chip);
        }
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _connection is null) return;
        ApplyMode();
    }

    private void ApplyMode()
    {
        bool dash = ModeDashboard.IsChecked == true;
        bool disks = ModeDisks.IsChecked == true;
        bool folders = ModeFolders.IsChecked == true;
        bool files = ModeFiles.IsChecked == true;
        bool mgmt = ModeMgmt.IsChecked == true;

        DashboardScroller.Visibility = dash ? Visibility.Visible : Visibility.Collapsed;
        ResultScroller.Visibility = disks ? Visibility.Visible : Visibility.Collapsed;
        FoldersPanel.Visibility = folders ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        MgmtScroller.Visibility = mgmt ? Visibility.Visible : Visibility.Collapsed;

        if (!dash) StopLive();   // المراقبة الحيّة تعمل فقط أثناء عرض لوحة القيادة

        if (dash)
        {
            _ = LoadDashboardAsync();
            if (LiveToggle.IsChecked == true) StartLive();
        }
        else if (mgmt && MgmtProcesses.ItemsSource == null) _ = LoadMgmtAsync();
        else if (disks && DisksList.ItemsSource == null) _ = ScanDisksAsync();
        else if (folders && FolderTree.Items.Count == 0 && !_suppressAutoScan)
            _ = ScanPathAsync(string.IsNullOrWhiteSpace(PathBox.Text) ? "/" : PathBox.Text.Trim());
        else if (files && _files.Count == 0 && !_suppressAutoScan)
            _ = ScanFilesAsync(string.IsNullOrWhiteSpace(FilesPathBox.Text) ? "/" : FilesPathBox.Text.Trim());
    }

    // يُمنع الفحص التلقائيّ في ApplyMode أثناء التنقّل الموجَّه (لتفادي فحص مزدوج للمسار)
    private bool _suppressAutoScan;

    private void ResetFolders()
    {
        FolderTree.Items.Clear();
        FolderSummary.Text = "";
        PathBox.Text = "/";
        _detailFolder = null;
        FolderFilesList.ItemsSource = null;
        FolderDetailHeader.Text = "";
        FolderDetailLoading.Visibility = Visibility.Collapsed;
        FolderDetailHint.Text = Loc.T("srv.folder.detailHint");
        FolderDetailEmpty.Visibility = Visibility.Visible;
    }

    private void ScanBtn_Click(object sender, RoutedEventArgs e)
        => _ = ScanPathAsync(PathBox.Text?.Trim() ?? "/");

    private void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _ = ScanPathAsync(PathBox.Text?.Trim() ?? "/"); e.Handled = true; }
    }

    /// <summary>يفحص مساراً ويبني جذر الشجرة بمجلّداته الفرعيّة المباشرة.</summary>
    private async Task ScanPathAsync(string path)
    {
        if (_connection is null) return;
        if (string.IsNullOrWhiteSpace(path)) path = "/";

        FolderSummary.Text = "";
        FolderScanLoading.Visibility = Visibility.Visible;
        FolderTree.Items.Clear();
        try
        {
            var scanner = new StorageScanner(_connection);
            var scan = await scanner.ScanSubfoldersAsync(path, _cts?.Token ?? default).ConfigureAwait(true);

            var root = FolderNode.Dir(scan.Path, scan.TotalBytes);
            root.Loaded = true;
            root.Children.Clear();
            foreach (var c in scan.Children)
                root.AddChild(FolderNode.Dir(c.Path, c.SizeBytes));

            FolderTree.Items.Add(root);
            FolderSummary.Text =
                $"{scan.Path}  ·  {Loc.T("srv.folder.total")} {DiskRowVm.Human(scan.TotalBytes)}  ·  " +
                $"{scan.Children.Count} {Loc.T("srv.folder.subfolders")}";

            if (FolderTree.ItemContainerGenerator.ContainerFromItem(root) is TreeViewItem tvi)
                tvi.IsExpanded = true;
        }
        catch (Exception ex)
        {
            FolderSummary.Text = ex.Message;
        }
        finally
        {
            FolderScanLoading.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>توسيع عقدة شجرة (EventSetter): sender هو الـ TreeViewItem نفسه لأيّ عمق.</summary>
    private async void TreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        e.Handled = true;   // لا نُمرّر الحدث لمعالِجات الآباء (تُستدعى بلا داعٍ)
        if (sender is TreeViewItem { DataContext: FolderNode node } && !node.Loaded && !node.Loading)
            await LoadChildrenAsync(node).ConfigureAwait(true);
    }

    /// <summary>تحميل كسول لمجلّدات عقدة عند توسيعها (يُظهر شريط تحميل inline ثم يستبدله بالأبناء).</summary>
    private async Task LoadChildrenAsync(FolderNode node)
    {
        if (_connection is null) return;
        node.Loading = true;
        node.Children.Clear();
        node.Children.Add(FolderNode.LoadingNode());   // شريط تحميل inline داخل العقدة
        try
        {
            var scanner = new StorageScanner(_connection);
            var scan = await scanner.ScanSubfoldersAsync(node.Path, _cts?.Token ?? default).ConfigureAwait(true);
            node.Children.Clear();   // إزالة شريط التحميل
            foreach (var c in scan.Children)
                node.AddChild(FolderNode.Dir(c.Path, c.SizeBytes));
            node.Loaded = true;
        }
        catch
        {
            node.Children.Clear();   // فشل → عقدة بلا أبناء
            node.Loaded = true;
        }
        finally
        {
            node.Loading = false;
        }
    }

    // ===== قائمة الشجرة السياقيّة + الربط بين اللوحات =====
    private FolderNode? _treeContextNode;

    private void Tree_PreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        // TreeView لا يختار بالزرّ الأيمن تلقائياً — نختار العقدة تحت المؤشّر قبل فتح القائمة.
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item != null)
        {
            item.IsSelected = true;
            _treeContextNode = item.DataContext as FolderNode;
        }
        else _treeContextNode = null;
    }

    /// <summary>يبني قائمةً سياقيّةً منفصلةً لكلّ شجرة (لا يمكن مشاركة نفس النسخة) بلغة/اتّجاه الواجهة.</summary>
    private void BuildTreeMenus()
    {
        FolderTree.ContextMenu = CreateTreeMenu();
        DashTree.ContextMenu = CreateTreeMenu();
    }

    private ContextMenu CreateTreeMenu()
    {
        var menu = new ContextMenu { FlowDirection = Loc.Flow };
        menu.Items.Add(TreeMenuItem("srv.tree.openFiles", "", TreeOpenInFiles_Click));
        menu.Items.Add(TreeMenuItem("srv.tree.openFolders", "", TreeOpenInFolders_Click));
        menu.Items.Add(TreeMenuItem("srv.file.copyPath", "", TreeMenuCopy_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(TreeMenuItem("srv.tree.refresh", "", TreeRefreshNode_Click));
        menu.Items.Add(TreeMenuItem("srv.tree.upload", "", TreeUpload_Click));
        menu.Items.Add(TreeMenuItem("srv.tree.newFolder", "", TreeMkdir_Click));
        menu.Items.Add(new Separator());
        var del = TreeMenuItem("srv.tree.deleteFolder", "", TreeDeleteFolder_Click);
        if (del.Icon is TextBlock tb) tb.Foreground = Brush("Brush.Danger");
        menu.Items.Add(del);
        return menu;
    }

    private static MenuItem TreeMenuItem(string key, string glyph, RoutedEventHandler handler)
    {
        var mi = new MenuItem
        {
            Header = Loc.T(key),
            Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14 },
        };
        mi.Click += handler;
        return mi;
    }

    private void TreeOpenInFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_treeContextNode is { IsPlaceholder: false } n) NavigateToFiles(n.Path);
    }
    private void TreeOpenInFolders_Click(object sender, RoutedEventArgs e)
    {
        if (_treeContextNode is { IsPlaceholder: false } n) NavigateToFolders(n.Path);
    }
    private void TreeMenuCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_treeContextNode is { IsPlaceholder: false } n) CopyPathWithToast(n.Path);
    }
    private void TreeRefreshNode_Click(object sender, RoutedEventArgs e)
    {
        if (_treeContextNode is { IsPlaceholder: false } n) { n.Loaded = false; _ = LoadChildrenAsync(n); }
    }

    private async void TreeDeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_treeContextNode is not { IsPlaceholder: false } n || _connection is null) return;
        if (n.Path == "/" || n.Parent is null)   // لا نحذف الجذر أو عقدةً بلا أمّ
        {
            NotificationService.Warning(Loc.T("srv.tree.deleteFolder"), Loc.T("srv.tree.cantDeleteRoot"));
            return;
        }
        if (!await ConfirmAsync(Loc.T("srv.tree.deleteFolder"), Loc.T("srv.tree.deleteFolderConfirm"), n.Path).ConfigureAwait(true))
            return;
        try
        {
            var ops = new FileOperations(_connection);
            await ops.DeleteFolderAsync(n.Path, _cts?.Token ?? default).ConfigureAwait(true);
            n.Parent?.Children.Remove(n);
            NotificationService.Secondary(Loc.T("srv.file.deleted"), NotificationType.Success);
        }
        catch (Exception ex)
        {
            NotificationService.Error(Loc.T("srv.tree.deleteFolder"), $"{Loc.T("srv.file.opFail")} {ex.Message}");
        }
    }

    private void TreeUpload_Click(object sender, RoutedEventArgs e)
    {
        if (_treeContextNode is { IsPlaceholder: false } n) _ = UploadToFolderAsync(n.Path);
    }

    private void TreeMkdir_Click(object sender, RoutedEventArgs e)
    {
        if (_treeContextNode is not { IsPlaceholder: false } n) return;
        OpenPrompt(Loc.T("srv.tree.newFolder"), "", name => MakeDirAsync(n, name));
    }

    private async Task MakeDirAsync(FolderNode parent, string name)
    {
        if (_connection is null) return;
        name = name.Trim();
        if (name.Length == 0 || name.Contains('/')) return;
        string path = parent.Path.TrimEnd('/') + "/" + name;
        try
        {
            await new FileOperations(_connection).MakeDirectoryAsync(path, _cts?.Token ?? default).ConfigureAwait(true);
            parent.Loaded = false;
            await LoadChildrenAsync(parent).ConfigureAwait(true);
            NotificationService.Secondary(Loc.T("srv.tree.folderCreated"), NotificationType.Success);
        }
        catch (Exception ex)
        {
            NotificationService.Error(Loc.T("srv.tree.newFolder"), ex.Message);
        }
    }

    /// <summary>ينتقل لتبويب «الملفّات» ويضبط المسار ويفحصه (ربط من القرص/الشجرة). فحص واحد مضمون.</summary>
    private void NavigateToFiles(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) path = "/";
        FilesPathBox.Text = path;
        _files = new List<FileRowVm>();
        _suppressAutoScan = true;          // بدّل التبويب دون فحص تلقائيّ…
        ModeFiles.IsChecked = true;
        _suppressAutoScan = false;
        _ = ScanFilesAsync(path);          // …ثم افحص المسار الجديد صراحةً (فحص واحد)
    }

    /// <summary>ينتقل لتبويب «المجلّدات» ويضبط المسار ويفحصه (ربط من القرص/الشجرة/لوحة القيادة).</summary>
    private void NavigateToFolders(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) path = "/";
        PathBox.Text = path;
        FolderTree.Items.Clear();
        _suppressAutoScan = true;
        ModeFolders.IsChecked = true;
        _suppressAutoScan = false;
        _ = ScanPathAsync(path);
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        return d as T;
    }

    private void CopyPathBtn_Click(object sender, RoutedEventArgs e)
    {
        string path = (FolderTree.SelectedItem as FolderNode)?.Path
            ?? (string.IsNullOrWhiteSpace(PathBox.Text) ? "/" : PathBox.Text.Trim());
        CopyPathWithToast(path);
    }

    private static void TryCopy(string text)
    {
        try { Clipboard.SetText(text); } catch { /* حافظة مشغولة */ }
    }

    // ===== توست سريع عبر نظام الإشعارات الموحّد (نمط هليوم) =====
    private static void ShowToast(string message)
        => NotificationService.Secondary(message, NotificationType.Success);

    // ===== أكبر الملفّات + العمليّات (الموجة 3) =====
    private void ResetFiles()
    {
        _files = new List<FileRowVm>();
        FilesGrid.ItemsSource = null;
        FilesCount.Text = "";
        FilesSearch.Text = "";
        FilesPathBox.Text = "/";
    }

    private FileRowVm? SelectedFile => FilesGrid.SelectedItem as FileRowVm;

    private void ScanFilesBtn_Click(object sender, RoutedEventArgs e)
        => _ = ScanFilesAsync(FilesPathBox.Text?.Trim() ?? "/");

    private void FilesPathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _ = ScanFilesAsync(FilesPathBox.Text?.Trim() ?? "/"); e.Handled = true; }
    }

    /// <summary>يفحص أكبر الملفّات تحت مسار ويملأ الجدول.</summary>
    private async Task ScanFilesAsync(string path)
    {
        if (_connection is null) return;
        if (string.IsNullOrWhiteSpace(path)) path = "/";

        FilesCount.Text = "";
        FilesGrid.ItemsSource = null;
        FilesEmpty.Visibility = Visibility.Collapsed;
        FilesLoading.Visibility = Visibility.Visible;
        try
        {
            var scanner = new StorageScanner(_connection);
            var files = await scanner.LargestFilesAsync(path, 200, _cts?.Token ?? default).ConfigureAwait(true);
            _files = files.Select(FileRowVm.From).ToList();
            ApplyFilesFilter();
        }
        catch (Exception ex)
        {
            _files = new List<FileRowVm>();
            FilesGrid.ItemsSource = null;
            FilesEmptyText.Text = ex.Message;
            FilesEmpty.Visibility = Visibility.Visible;
        }
        finally
        {
            FilesLoading.Visibility = Visibility.Collapsed;
        }
    }

    private void FilesSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilesFilter();

    private void ApplyFilesFilter()
    {
        string q = FilesSearch.Text?.Trim() ?? "";
        IEnumerable<FileRowVm> view = _files;
        if (q.Length > 0)
            view = _files.Where(f =>
                f.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                f.Path.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                f.Ext.Contains(q, StringComparison.OrdinalIgnoreCase));

        var list = view.ToList();
        FilesGrid.ItemsSource = list;

        // فرز افتراضيّ تنازليّ بالحجم (الأكبر أوّلاً) + إظهار سهم الفرز على عمود الحجم.
        // نقر أيّ ترويسة يعيد الفرز تصاعديّاً/تنازليّاً (آليّة DataGrid القياسيّة).
        FilesGrid.Items.SortDescriptions.Clear();
        FilesGrid.Items.SortDescriptions.Add(new SortDescription(nameof(FileRowVm.SizeBytes), ListSortDirection.Descending));
        foreach (var col in FilesGrid.Columns) col.SortDirection = null;
        ColSize.SortDirection = ListSortDirection.Descending;
        FilesGrid.Items.Refresh();

        FilesCount.Text = $"{list.Count} {Loc.T("srv.files.filesWord")}";

        // حالة الفراغ: «لا ملفّات» إن لم يُفحَص شيء، أو «لا نتائج للبحث» إن حجبها الفلتر.
        if (list.Count == 0)
        {
            FilesEmptyText.Text = _files.Count == 0 ? Loc.T("srv.files.empty") : Loc.T("srv.files.noMatch");
            FilesEmpty.Visibility = Visibility.Visible;
        }
        else FilesEmpty.Visibility = Visibility.Collapsed;
    }

    private void FilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // نقر مزدوج على ملفّ = نسخ مساره + توست (العرض/التنزيل عبر القائمة السياقيّة).
        if (SelectedFile is { } f) CopyPathWithToast(f.Path);
    }

    private void FileCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFile is { } f) CopyPathWithToast(f.Path);
    }

    private void CopyPathWithToast(string path)
    {
        TryCopy(path);
        ShowToast(Loc.T("srv.toast.copied"));
    }

    // ===== تنزيل عبر SFTP (مفرد أو مجمّع حسب التحديد) =====
    private List<FileRowVm> GridSelection() => FilesGrid.SelectedItems.OfType<FileRowVm>().ToList();

    private void FileDownload_Click(object sender, RoutedEventArgs e)
    {
        var sel = GridSelection();
        if (sel.Count > 1) _ = DownloadManyAsync(sel);
        else if (sel.Count == 1) _ = DownloadFileAsync(sel[0]);
    }

    private async Task DownloadFileAsync(FileRowVm f)
    {
        if (_connectedProfile is null) return;

        var dlg = new SaveFileDialog { FileName = f.Name };
        if (dlg.ShowDialog(this) != true) return;

        // بطاقة تقدّم حيّة (سرعة + متبقٍّ + شريط) بزرّ إلغاء/إغلاق. الإلغاء يتخلّص من عميل SFTP
        // فيُجهَض التنزيل الجاري (SSH.NET المتزامن لا يقبل CancellationToken أثناء النقل).
        var sftp = new SshNetSftp(_service.BuildConnectionInfo(_connectedProfile));
        bool cancelled = false;
        var progress = NotificationService.Progress(
            $"{Loc.T("srv.dl.downloading")} {f.Name}", "0%",
            onCancel: () => { cancelled = true; try { sftp.Dispose(); } catch { } });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        long total = f.SizeBytes;
        try
        {
            await sftp.ConnectAsync(_cts?.Token ?? default).ConfigureAwait(true);
            await sftp.DownloadAsync(f.Path, dlg.FileName, downloaded =>
            {
                double secs = sw.Elapsed.TotalSeconds;
                double frac = total > 0 ? (double)downloaded / total : 0;
                double speed = secs > 0 ? downloaded / secs : 0;
                string detail = total > 0
                    ? $"{DiskRowVm.Human((long)downloaded)} / {DiskRowVm.Human(total)}  ·  {DiskRowVm.Human((long)speed)}/s  ·  {frac * 100:0}%"
                    : $"{DiskRowVm.Human((long)downloaded)}  ·  {DiskRowVm.Human((long)speed)}/s";
                progress.Report(frac, detail);
            }, _cts?.Token ?? default).ConfigureAwait(true);
            progress.Done(Loc.T("srv.file.download"), $"{f.Name} — {Loc.T("srv.file.downloadOk")}");
        }
        catch (Exception ex)
        {
            if (cancelled)
                NotificationService.Secondary(Loc.T("srv.dl.cancelled"), NotificationType.Warning);
            else
                progress.Fail(Loc.T("srv.file.download"), $"{Loc.T("srv.file.downloadFail")} {ex.Message}");
        }
        finally
        {
            try { sftp.Dispose(); } catch { }
        }
    }

    // ===== تفاصيل المجلّد المختار (ملفّاته المباشرة) =====
    private async void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FolderNode node || node.IsPlaceholder) return;
        await LoadFolderFilesAsync(node).ConfigureAwait(true);
    }

    private async Task LoadFolderFilesAsync(FolderNode node)
    {
        if (_connection is null) return;
        _detailFolder = node;
        FolderDetailHeader.Text = $"{Loc.T("srv.folder.filesIn")} {node.Path}";
        FolderFilesList.ItemsSource = null;
        SetDetailState(loading: true, emptyText: null);
        try
        {
            var scanner = new StorageScanner(_connection);
            var files = await scanner.ListFilesAsync(node.Path, _cts?.Token ?? default).ConfigureAwait(true);
            var rows = files.Select(FileRowVm.From).ToList();
            FolderFilesList.ItemsSource = rows;
            FolderDetailHeader.Text = $"{Loc.T("srv.folder.filesIn")} {node.Path}  ·  {rows.Count}";
            SetDetailState(loading: false, emptyText: rows.Count == 0 ? Loc.T("srv.folder.noFiles") : null);
        }
        catch (Exception ex)
        {
            SetDetailState(loading: false, emptyText: ex.Message);
        }
    }

    /// <summary>يبدّل حالة لوحة التفاصيل: مؤشّر تحميل، أو رسالة فراغ (بأيقونة)، أو القائمة.</summary>
    private void SetDetailState(bool loading, string? emptyText)
    {
        FolderDetailLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        FolderDetailEmpty.Visibility = !loading && emptyText != null ? Visibility.Visible : Visibility.Collapsed;
        if (emptyText != null) FolderDetailHint.Text = emptyText;
    }

    private FileRowVm? DetailFile => FolderFilesList.SelectedItem as FileRowVm;

    private Task ReloadDetailAsync() => _detailFolder is { } n ? LoadFolderFilesAsync(n) : Task.CompletedTask;

    private void FolderFilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DetailFile is { } f) CopyPathWithToast(f.Path);
    }
    private List<FileRowVm> DetailSelection() => FolderFilesList.SelectedItems.OfType<FileRowVm>().ToList();

    private void DetailDownload_Click(object sender, RoutedEventArgs e)
    {
        var sel = DetailSelection();
        if (sel.Count > 1) _ = DownloadManyAsync(sel);
        else if (sel.Count == 1) _ = DownloadFileAsync(sel[0]);
    }
    private void DetailView_Click(object sender, RoutedEventArgs e)
    {
        if (DetailFile is { } f) _ = OpenLogAsync(f);
    }
    private void DetailRename_Click(object sender, RoutedEventArgs e)
    {
        if (DetailFile is { } f) OpenRename(f, (_, _) => ReloadDetailAsync());
    }
    private void DetailDelete_Click(object sender, RoutedEventArgs e)
    {
        var sel = DetailSelection();
        if (sel.Count > 1) _ = DeleteManyAsync(sel, ReloadDetailAsync);
        else if (sel.Count == 1) _ = DeleteFileAsync(sel[0], ReloadDetailAsync);
    }
    private void DetailCopy_Click(object sender, RoutedEventArgs e)
    {
        if (DetailFile is { } f) CopyPathWithToast(f.Path);
    }

    // ===== حذف (مفرد أو مجمّع؛ يعمل على الجدول أو لوحة التفاصيل عبر ردّ التحديث) =====
    private void FileDelete_Click(object sender, RoutedEventArgs e)
    {
        var sel = GridSelection();
        Func<Task> refresh = () => { foreach (var x in sel) _files.Remove(x); ApplyFilesFilter(); return Task.CompletedTask; };
        if (sel.Count > 1) _ = DeleteManyAsync(sel, refresh);
        else if (sel.Count == 1) _ = DeleteFileAsync(sel[0], refresh);
    }

    /// <summary>تنزيل عدّة ملفّات إلى مجلّد محلّيّ واحد ببطاقة تقدّم (ملفّ/الإجماليّ) وإلغاء.</summary>
    private async Task DownloadManyAsync(List<FileRowVm> files)
    {
        if (_connectedProfile is null || files.Count == 0) return;
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog(this) != true) return;
        string dir = dlg.FolderName;

        var sftp = new SshNetSftp(_service.BuildConnectionInfo(_connectedProfile));
        bool cancelled = false;
        var progress = NotificationService.Progress($"{Loc.T("srv.dl.downloading")} · {files.Count}", $"0/{files.Count}",
            onCancel: () => { cancelled = true; try { sftp.Dispose(); } catch { } });
        try
        {
            await sftp.ConnectAsync(_cts?.Token ?? default).ConfigureAwait(true);
            int done = 0;
            foreach (var f in files)
            {
                if (cancelled) break;
                await sftp.DownloadAsync(f.Path, UniqueLocal(dir, f.Name), null, _cts?.Token ?? default).ConfigureAwait(true);
                progress.Report(++done / (double)files.Count, $"{done}/{files.Count}");
            }
            if (cancelled) NotificationService.Secondary(Loc.T("srv.dl.cancelled"), NotificationType.Warning);
            else progress.Done(Loc.T("srv.file.download"), $"{files.Count} · {dir}");
        }
        catch (Exception ex)
        {
            if (cancelled) NotificationService.Secondary(Loc.T("srv.dl.cancelled"), NotificationType.Warning);
            else progress.Fail(Loc.T("srv.file.download"), $"{Loc.T("srv.file.downloadFail")} {ex.Message}");
        }
        finally { try { sftp.Dispose(); } catch { } }
    }

    /// <summary>اسم محلّيّ فريد (يُلحق «(2)» إن وُجد ملفّ بنفس الاسم).</summary>
    private static string UniqueLocal(string dir, string name)
    {
        string p = Path.Combine(dir, name);
        if (!File.Exists(p)) return p;
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            string cand = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(cand)) return cand;
        }
    }

    /// <summary>رفع ملفّات محلّيّة إلى مجلّد بعيد عبر SFTP ببطاقة تقدّم وإلغاء.</summary>
    private async Task UploadToFolderAsync(string remoteDir)
    {
        if (_connectedProfile is null) return;
        var dlg = new OpenFileDialog { Multiselect = true };
        if (dlg.ShowDialog(this) != true || dlg.FileNames.Length == 0) return;
        var locals = dlg.FileNames;
        string dir = remoteDir.TrimEnd('/');

        var sftp = new SshNetSftp(_service.BuildConnectionInfo(_connectedProfile));
        bool cancelled = false;
        var progress = NotificationService.Progress($"{Loc.T("srv.up.uploading")} · {locals.Length}", $"0/{locals.Length}",
            onCancel: () => { cancelled = true; try { sftp.Dispose(); } catch { } });
        try
        {
            await sftp.ConnectAsync(_cts?.Token ?? default).ConfigureAwait(true);
            int done = 0;
            foreach (var lf in locals)
            {
                if (cancelled) break;
                await sftp.UploadAsync(lf, dir + "/" + Path.GetFileName(lf), null, _cts?.Token ?? default).ConfigureAwait(true);
                progress.Report(++done / (double)locals.Length, $"{done}/{locals.Length}");
            }
            if (cancelled) { NotificationService.Secondary(Loc.T("srv.up.cancelled"), NotificationType.Warning); return; }
            progress.Done(Loc.T("srv.up.title"), $"{locals.Length} → {remoteDir}");
            if (_detailFolder?.Path == remoteDir) await ReloadDetailAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (cancelled) NotificationService.Secondary(Loc.T("srv.up.cancelled"), NotificationType.Warning);
            else progress.Fail(Loc.T("srv.up.title"), ex.Message);
        }
        finally { try { sftp.Dispose(); } catch { } }
    }

    /// <summary>حذف عدّة ملفّات بتأكيد واحد؛ يُبلّغ بعدد الناجح/الفاشل.</summary>
    private async Task DeleteManyAsync(List<FileRowVm> files, Func<Task> refresh)
    {
        if (_connection is null || files.Count == 0) return;
        string preview = string.Join("\n", files.Take(12).Select(f => f.Path)) + (files.Count > 12 ? "\n…" : "");
        if (!await ConfirmAsync(Loc.T("srv.file.deleteTitle"), $"{Loc.T("srv.file.deleteManyConfirm")} ({files.Count})", preview).ConfigureAwait(true))
            return;

        var ops = new FileOperations(_connection);
        int ok = 0, fail = 0;
        foreach (var f in files)
        {
            try { await ops.DeleteAsync(f.Path, _cts?.Token ?? default).ConfigureAwait(true); ok++; }
            catch { fail++; }
        }
        await refresh().ConfigureAwait(true);
        if (fail == 0) NotificationService.Secondary($"{Loc.T("srv.file.deleted")} · {ok}", NotificationType.Success);
        else NotificationService.Warning(Loc.T("srv.file.delete"), $"{ok} ✓ · {fail} ✗");
    }

    private async Task DeleteFileAsync(FileRowVm f, Func<Task> refresh)
    {
        if (_connection is null) return;
        if (!await ConfirmAsync(Loc.T("srv.file.deleteTitle"), Loc.T("srv.file.deleteConfirm"), f.Path).ConfigureAwait(true))
            return;

        try
        {
            var ops = new FileOperations(_connection);
            await ops.DeleteAsync(f.Path, _cts?.Token ?? default).ConfigureAwait(true);
            await refresh().ConfigureAwait(true);
            NotificationService.Secondary(Loc.T("srv.file.deleted"), NotificationType.Success);
        }
        catch (Exception ex)
        {
            NotificationService.Error(Loc.T("srv.file.delete"), $"{Loc.T("srv.file.opFail")} {ex.Message}");
        }
    }

    // ===== إعادة تسمية =====
    private void FileRename_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFile is { } f)
            OpenRename(f, (np, nn) =>
            {
                int idx = _files.IndexOf(f);
                if (idx >= 0) _files[idx] = f.WithRename(nn, np);
                ApplyFilesFilter();
                return Task.CompletedTask;
            });
    }

    private void OpenRename(FileRowVm f, Func<string, string, Task> refresh)
    {
        _promptAction = null;
        _renameTarget = f;
        _renameRefresh = refresh;
        RenameTitle.Text = Loc.T("srv.file.rename");
        RenameBox.Text = f.Name;
        RenameOverlay.Visibility = Visibility.Visible;
        RenameBox.Focus();
        RenameBox.SelectAll();
    }

    /// <summary>حوار إدخال نصّ عامّ (يعيد استعمال حوار إعادة التسمية) — يُستعمل لاسم المجلّد الجديد.</summary>
    private Func<string, Task>? _promptAction;
    private void OpenPrompt(string title, string initial, Func<string, Task> onOk)
    {
        _renameTarget = null;
        _promptAction = onOk;
        RenameTitle.Text = title;
        RenameBox.Text = initial;
        RenameOverlay.Visibility = Visibility.Visible;
        RenameBox.Focus();
        RenameBox.SelectAll();
    }

    private void RenameCancel_Click(object sender, RoutedEventArgs e) => CloseRename();
    private void RenameOverlay_MouseDown(object sender, MouseButtonEventArgs e) => CloseRename();
    private void CloseRename() { _promptAction = null; RenameOverlay.Visibility = Visibility.Collapsed; }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { RenameSave_Click(sender, e); e.Handled = true; }
    }

    private async void RenameSave_Click(object sender, RoutedEventArgs e)
    {
        // وضع الإدخال العامّ (مثل «مجلّد جديد»)
        if (_promptAction is { } action)
        {
            string val = RenameBox.Text.Trim();
            var a = action;
            CloseRename();
            if (val.Length > 0) await a(val).ConfigureAwait(true);
            return;
        }

        if (_renameTarget is not { } f || _connection is null) return;
        string newName = RenameBox.Text.Trim();
        if (newName.Length == 0 || newName == f.Name) { RenameOverlay.Visibility = Visibility.Collapsed; return; }
        if (newName.Contains('/')) { RenameBox.Focus(); return; }   // اسم لا مسار

        int slash = f.Path.LastIndexOf('/');
        string dir = slash >= 0 ? f.Path.Substring(0, slash) : "";
        string newPath = (dir.Length == 0 ? "" : dir + "/") + newName;

        RenameOverlay.Visibility = Visibility.Collapsed;
        try
        {
            var ops = new FileOperations(_connection);
            await ops.RenameAsync(f.Path, newPath, _cts?.Token ?? default).ConfigureAwait(true);
            if (_renameRefresh is { } r) await r(newPath, newName).ConfigureAwait(true);
            NotificationService.Secondary(Loc.T("srv.file.renamed"), NotificationType.Success);
        }
        catch (Exception ex)
        {
            NotificationService.Error(Loc.T("srv.file.rename"), $"{Loc.T("srv.file.opFail")} {ex.Message}");
        }
    }

    // ===== تصدير CSV =====
    private void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.ItemsSource is not IEnumerable<FileRowVm> rows) return;
        var list = rows.ToList();
        if (list.Count == 0) return;

        var dlg = new SaveFileDialog { FileName = "largest-files.csv", Filter = "CSV|*.csv" };
        if (dlg.ShowDialog(this) != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("Name,Extension,SizeBytes,Modified,Path");
        foreach (var f in list)
            sb.AppendLine(string.Join(',',
                Csv(f.Name), Csv(f.Ext), f.SizeBytes.ToString(CultureInfo.InvariantCulture),
                Csv(f.ModifiedText), Csv(f.Path)));
        try
        {
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            NotificationService.Success(Loc.T("srv.files.export"), $"{list.Count} {Loc.T("srv.files.filesWord")}");
        }
        catch (Exception ex)
        {
            NotificationService.Error(Loc.T("srv.files.export"), $"{Loc.T("srv.file.opFail")} {ex.Message}");
        }
    }

    private static string Csv(string value)
    {
        value ??= "";
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    // ===== عارض السجلّ (tail + بحث) =====
    private async Task OpenLogAsync(FileRowVm f)
    {
        if (_connection is null) return;
        LogTitle.Text = f.Path;
        LogContent.Text = Loc.T("srv.file.loading");
        LogSearch.Text = "";
        _logSearchFrom = 0;
        LogOverlay.Visibility = Visibility.Visible;
        try
        {
            var ops = new FileOperations(_connection);
            string tail = await ops.TailAsync(f.Path, 1000, _cts?.Token ?? default).ConfigureAwait(true);
            LogContent.Text = tail.Length == 0 ? "—" : tail;
        }
        catch (Exception ex)
        {
            LogContent.Text = ex.Message;
        }
    }

    private void FileViewLog_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFile != null) _ = OpenLogAsync(SelectedFile);
    }

    // ===== اعرف الحاوية (Docker) — يربط مسار ملفّ بالحاوية المالكة له =====
    private void FileWhichContainer_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFile is { } f) _ = ResolveContainerAsync(f);
    }

    private void DetailWhichContainer_Click(object sender, RoutedEventArgs e)
    {
        if (DetailFile is { } f) _ = ResolveContainerAsync(f);
    }

    private string _containerCopyName = "";
    private string _containerCopyId = "";

    /// <summary>يحدّد حاوية Docker المالكة لمسار الملفّ ويعرضها في لوحة مخصّصة.</summary>
    private async Task ResolveContainerAsync(FileRowVm f)
    {
        if (_connection is null) return;

        ContainerPath.Text = f.Path;
        ContainerName.Text = Loc.T("srv.docker.resolving");
        ContainerKindLine.Text = "";
        ContainerList.ItemsSource = null;
        ContainerEmpty.Visibility = Visibility.Collapsed;
        ContainerBody.Visibility = Visibility.Collapsed;
        ContainerLoading.Visibility = Visibility.Visible;
        ContainerCopyName.IsEnabled = false;
        _containerCopyName = "";
        _containerCopyId = "";
        ContainerOverlay.Visibility = Visibility.Visible;
        try
        {
            var lookup = await new DockerInspector(_connection)
                .ResolveAsync(f.Path, _cts?.Token ?? default).ConfigureAwait(true);
            PopulateContainer(lookup);
        }
        catch (Exception ex)
        {
            ContainerName.Text = "—";
            ContainerKindLine.Text = "";
            ContainerList.ItemsSource = null;
            ContainerEmptyText.Text = $"{Loc.T("srv.file.opFail")} {ex.Message}";
            ContainerEmpty.Visibility = Visibility.Visible;
        }
        finally
        {
            ContainerLoading.Visibility = Visibility.Collapsed;
            ContainerBody.Visibility = Visibility.Visible;
        }
    }

    /// <summary>يملأ لوحة معلومات الحاوية من نتيجة الاستعلام.</summary>
    private void PopulateContainer(DockerLookup lookup)
    {
        var primary = lookup.Matches.FirstOrDefault(m => m.WritableLayer) ?? lookup.Matches.FirstOrDefault();
        ContainerName.Text = primary is null ? "—" : NiceContainer(primary);
        _containerCopyName = primary?.Name ?? "";
        _containerCopyId = lookup.Key;
        ContainerCopyName.IsEnabled = _containerCopyName.Length > 0;
        ContainerCopyId.IsEnabled = _containerCopyId.Length > 0;

        ContainerKindLine.Text = lookup.Kind switch
        {
            DockerPathKind.Overlay => $"{Loc.T("srv.docker.overlayLayer")} · {lookup.Key}",
            DockerPathKind.Volume => $"{Loc.T("srv.docker.volume")} · {lookup.Key}",
            _ => Loc.T("srv.docker.notDocker"),
        };

        if (lookup.Matches.Count == 0)
        {
            ContainerList.ItemsSource = null;
            ContainerEmptyText.Text = lookup.Kind switch
            {
                DockerPathKind.Overlay => Loc.T("srv.docker.noOwner"),
                DockerPathKind.Volume => Loc.T("srv.docker.noVolOwner"),
                _ => Loc.T("srv.docker.notDocker"),
            };
            ContainerEmpty.Visibility = Visibility.Visible;
        }
        else
        {
            ContainerList.ItemsSource = lookup.Matches.Select(ContainerCardVm.From).ToList();
            ContainerEmpty.Visibility = Visibility.Collapsed;
            ShowToast($"{Loc.T("srv.file.whichContainer")} · {ContainerName.Text}");
        }
    }

    /// <summary>أفضل اسم يُعرض للحاوية (Coolify > Compose > اسم الحاوية).</summary>
    private static string NiceContainer(DockerContainerMatch m)
        => !string.IsNullOrEmpty(m.CoolifyName) ? m.CoolifyName
         : !string.IsNullOrEmpty(m.ComposeProject) ? m.ComposeProject
         : m.Name;

    private void ContainerClose_Click(object sender, RoutedEventArgs e) => ContainerOverlay.Visibility = Visibility.Collapsed;
    private void ContainerOverlay_MouseDown(object sender, MouseButtonEventArgs e) => ContainerOverlay.Visibility = Visibility.Collapsed;

    private void ContainerCopyName_Click(object sender, RoutedEventArgs e)
    {
        if (_containerCopyName.Length == 0) return;
        TryCopy(_containerCopyName);
        ShowToast(Loc.T("srv.toast.copied"));
    }

    private void ContainerCopyId_Click(object sender, RoutedEventArgs e)
    {
        if (_containerCopyId.Length == 0) return;
        TryCopy(_containerCopyId);
        ShowToast(Loc.T("srv.toast.copied"));
    }

    private void LogClose_Click(object sender, RoutedEventArgs e) => LogOverlay.Visibility = Visibility.Collapsed;
    private void LogOverlay_MouseDown(object sender, MouseButtonEventArgs e) => LogOverlay.Visibility = Visibility.Collapsed;

    private void LogSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        string term = LogSearch.Text;
        if (string.IsNullOrEmpty(term)) return;

        string text = LogContent.Text;
        int idx = text.IndexOf(term, _logSearchFrom, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 && _logSearchFrom > 0) idx = text.IndexOf(term, 0, StringComparison.OrdinalIgnoreCase); // التفاف
        if (idx < 0) return;

        LogContent.Focus();
        LogContent.Select(idx, term.Length);
        var rect = LogContent.GetRectFromCharacterIndex(idx);
        LogContent.ScrollToVerticalOffset(LogContent.VerticalOffset + rect.Top - 40);
        _logSearchFrom = idx + term.Length;
    }

    // ===== شريط العنوان =====
    private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaxButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ===== أدوات =====
    private Brush Brush(string key) => (Brush)FindResource(key);

    private static Brush ParseBrush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.Gray; }
    }
}

/// <summary>صفّ قرص للعرض: نصوص منسّقة + نسبة الاستخدام + لون الشريط حسب الامتلاء.</summary>
public sealed class DiskRowVm
{
    public string Filesystem { get; init; } = "";
    public string Mount { get; init; } = "";
    public double UsePercent { get; init; }
    public string SummaryText { get; init; } = "";
    public Brush BarBrush { get; init; } = Brushes.SteelBlue;

    public static DiskRowVm From(DiskInfo d)
    {
        Brush bar = d.UsePercent >= 90
            ? Fixed("#EF4444")
            : d.UsePercent >= 75 ? Fixed("#F59E0B") : ThemeAccent();
        return new DiskRowVm
        {
            Filesystem = d.Filesystem,
            Mount = d.MountPoint,
            UsePercent = d.UsePercent,
            SummaryText = $"{Human(d.UsedBytes)} / {Human(d.TotalBytes)}  ·  {d.UsePercent:0.#}%",
            BarBrush = bar,
        };
    }

    private static Brush ThemeAccent()
        => Application.Current?.TryFindResource("Brush.Accent") as Brush ?? Fixed("#3B82F6");

    private static Brush Fixed(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.SteelBlue; }
    }

    /// <summary>تنسيق حجم بالبايت إلى وحدة مقروءة (B/KB/MB/GB/TB).</summary>
    public static string Human(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return string.Format(CultureInfo.InvariantCulture, u == 0 ? "{0:0} {1}" : "{0:0.#} {1}", v, units[u]);
    }
}

/// <summary>
/// عقدة مجلّد في شجرة المستكشف. الأبناء <see cref="Children"/> observable فتُحدَّث الشجرة تلقائياً؛
/// التحميل كسول: كلّ مجلّد يبدأ بعنصر نائب ليُظهر سهم التوسيع، ويُستبدل عند التوسيع.
/// </summary>
public sealed class FolderNode
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool Loaded { get; set; }
    public bool Loading { get; set; }

    /// <summary>العقدة الأمّ (لإزالة العقدة من الشجرة بعد حذف مجلّدها).</summary>
    public FolderNode? Parent { get; set; }

    /// <summary>يضيف ابناً ويضبط أمّه.</summary>
    public void AddChild(FolderNode child) { child.Parent = this; Children.Add(child); }

    public System.Collections.ObjectModel.ObservableCollection<FolderNode> Children { get; } = new();

    public string DisplayName => Name;
    public string SizeText => IsPlaceholder ? "" : DiskRowVm.Human(SizeBytes);
    public bool IsPlaceholder => Path.Length == 0;

    /// <summary>عقدة تمثّل حالة تحميل (تُعرَض شريطَ تقدّم inline في القالب).</summary>
    public bool IsLoadingNode { get; set; }

    /// <summary>عقدة تحميل مؤقّتة تظهر أثناء جلب أبناء عقدة.</summary>
    public static FolderNode LoadingNode() => new() { Name = "…", IsLoadingNode = true };

    /// <summary>ينشئ عقدة مجلّد باسم مشتقّ من مساره + عنصر نائب لإظهار سهم التوسيع.</summary>
    public static FolderNode Dir(string path, long size)
    {
        string trimmed = path.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        string name = slash >= 0 && slash < trimmed.Length - 1 ? trimmed[(slash + 1)..] : path;
        if (string.IsNullOrEmpty(name)) name = path;

        var node = new FolderNode { Path = path, Name = name, SizeBytes = size };
        node.Children.Add(new FolderNode { Name = "…" });   // نائب
        return node;
    }
}

/// <summary>صفّ ملفّ في جدول «أكبر الملفّات»: نصوص عرض + مفاتيح فرز (بايت/تاريخ) للأعمدة القابلة للفرز.</summary>
public sealed class FileRowVm
{
    public string Name { get; init; } = "";
    public string Ext { get; init; } = "";
    public string Path { get; init; } = "";
    public long SizeBytes { get; init; }
    public string SizeText => DiskRowVm.Human(SizeBytes);
    public DateTimeOffset? ModifiedSort { get; init; }
    public string ModifiedText => ModifiedSort?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "";

    public static FileRowVm From(FileEntry f) => new()
    {
        Name = f.Name,
        Ext = ExtOf(f.Name),
        Path = f.Path,
        SizeBytes = f.SizeBytes,
        ModifiedSort = f.Modified,
    };

    /// <summary>نسخة بعد إعادة تسمية (الحجم/التاريخ يبقيان).</summary>
    public FileRowVm WithRename(string newName, string newPath) => new()
    {
        Name = newName,
        Ext = ExtOf(newName),
        Path = newPath,
        SizeBytes = SizeBytes,
        ModifiedSort = ModifiedSort,
    };

    private static string ExtOf(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..] : "";
    }
}

/// <summary>صفّ Treemap: مجلّد بشريط نسبيّ (النسبة إلى أكبر مجلّد) + لون من لوحة متنوّعة.</summary>
public sealed class TreemapRowVm
{
    private static readonly string[] Palette =
        { "#3B82F6", "#22C55E", "#F59E0B", "#A855F7", "#EF4444", "#14B8A6", "#EC4899", "#64748B" };

    public string Label { get; init; } = "";
    public string SizeText { get; init; } = "";
    public double Percent { get; init; }
    public Brush BarBrush { get; init; } = Brushes.SteelBlue;

    public static TreemapRowVm From(DirEntry d, long max, int index)
    {
        double pct = max > 0 ? d.SizeBytes * 100.0 / max : 0;
        Brush brush;
        try { brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Palette[index % Palette.Length])); }
        catch { brush = Brushes.SteelBlue; }
        return new TreemapRowVm
        {
            Label = d.Path,
            SizeText = DiskRowVm.Human(d.SizeBytes),
            Percent = pct,
            BarBrush = brush,
        };
    }
}

/// <summary>صفّ عمليّة في لوحة القيادة: نصوص CPU/MEM + الأمر.</summary>
public sealed class ProcRowVm
{
    public string CpuText { get; init; } = "";
    public string MemText { get; init; } = "";
    public string Command { get; init; } = "";

    public static ProcRowVm From(ProcessInfo p) => new()
    {
        CpuText = $"{p.CpuPercent.ToString("0.#", CultureInfo.InvariantCulture)}%",
        MemText = $"{p.MemPercent.ToString("0.#", CultureInfo.InvariantCulture)}%",
        Command = p.Command,
    };
}

/// <summary>صفّ عمليّة في تبويب الإدارة (بـ PID + مفاتيح فرز CPU/MEM).</summary>
public sealed class MgmtProcVm
{
    public int Pid { get; init; }
    public string User { get; init; } = "";
    public double Cpu { get; init; }
    public double Mem { get; init; }
    public string Command { get; init; } = "";
    public string CpuText => Cpu.ToString("0.#", CultureInfo.InvariantCulture);
    public string MemText => Mem.ToString("0.#", CultureInfo.InvariantCulture);

    public static MgmtProcVm From(ProcessInfo p) => new()
    { Pid = p.Pid, User = p.User, Cpu = p.CpuPercent, Mem = p.MemPercent, Command = p.Command };
}

/// <summary>صفّ خدمة systemd (الاسم + الحالة active/sub + الوصف).</summary>
public sealed class ServiceRowVm
{
    public string Name { get; init; } = "";
    public string ActiveText { get; init; } = "";
    public string Description { get; init; } = "";

    public static ServiceRowVm From(ServiceInfo s) => new()
    { Name = s.Name, ActiveText = $"{s.Active}/{s.Sub}", Description = s.Description };
}

/// <summary>صفّ منفذ مُنصِت (المنفذ + العنوان + العمليّة).</summary>
public sealed class PortRowVm
{
    public int Port { get; init; }
    public string Address { get; init; } = "";
    public string Process { get; init; } = "";
}

/// <summary>بطاقة حاوية Docker في لوحة «اعرف الحاوية» — نصوصها مُترجَمة لحظة الإنشاء (اللغة الحاليّة).</summary>
public sealed class ContainerCardVm
{
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public string Image { get; init; } = "";
    public string CoolifyName { get; init; } = "";
    public string ComposeProject { get; init; } = "";
    public string OwnershipText { get; init; } = "";
    public string ImageLabel { get; init; } = "";
    public bool Running { get; init; }
    public bool HasImage => !string.IsNullOrEmpty(Image);
    public bool HasCoolify => !string.IsNullOrEmpty(CoolifyName);
    public bool HasCompose => !string.IsNullOrEmpty(ComposeProject);

    public static ContainerCardVm From(DockerContainerMatch m) => new()
    {
        Name = m.Name,
        Status = m.Status,
        Image = m.Image,
        CoolifyName = m.CoolifyName,
        ComposeProject = m.ComposeProject,
        Running = string.Equals(m.Status, "running", System.StringComparison.OrdinalIgnoreCase),
        OwnershipText = m.WritableLayer ? Loc.T("srv.docker.rwLayer") : Loc.T("srv.docker.mounted"),
        ImageLabel = Loc.T("srv.docker.image"),
    };
}
