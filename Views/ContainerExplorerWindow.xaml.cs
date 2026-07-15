using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Terminal.Servers.Models;
using Terminal.Servers.Scan;
using Terminal.Servers.Ssh;
using TerminalLauncher.Controls;
using TerminalLauncher.Models;
using TerminalLauncher.Services;

namespace TerminalLauncher.Views;

/// <summary>
/// مستكشف حاوية Docker: يجمع تصفّح ملفّات الحاوية (عبر <c>docker exec … ls</c>) + كونسول تفاعليّ كامل
/// داخلها (عبر <see cref="ContainerShell"/> فوق ssh.exe) في نافذة واحدة. يملك اتّصال SSH خاصّاً به
/// للتصفّح (مستقلّ عن مراقب الخوادم)، والكونسول قناة ssh.exe منفصلة — فيبقيان يعملان مستقلَّين.
/// </summary>
public partial class ContainerExplorerWindow : Window
{
    private readonly string _containerId;
    private readonly bool _sudo;
    private readonly SshConnectionInfo _info;
    private readonly SshNetConnection _browseConn;
    private readonly ContainerFiles _files;

    // ===== تبويبات الكونسول (عدّة تيرمنالات داخل الحاوية) =====
    private readonly ObservableCollection<ConsoleTabVm> _consoleTabs = new();
    private ConsoleTabVm? _activeConsole;
    private int _consoleSeq;
    private TerminalTabView? ActiveConsoleView => _activeConsole?.View;

    private string _path = "/";
    private string _viewerPath = "";   // مسار الملفّ المعروض حاليّاً (لزرّ تنزيل العارض)
    private bool _viewerIsBinary;      // الملفّ المعروض ثنائيّ (يمنع التعديل)
    private bool _editing;             // وضع تحرير العارض نشط
    private CancellationTokenSource? _cts;
    private bool _busy;

    // ===== فرز قائمة الملفّات (المجلّدات دائماً أوّلاً، ثمّ حسب العمود المختار) =====
    private enum SortKey { Name, Type, Size, Modified }
    private SortKey _sortKey = SortKey.Name;
    private bool _sortAsc = true;
    private List<ContainerFileVm> _currentFiles = new();

    // ===== حالة تخطيط الكونسول =====
    private enum ConsoleState { Split, Collapsed, Maximized }
    private ConsoleState _consoleState = ConsoleState.Split;
    private Orientation _consoleOrientation = Orientation.Vertical;   // Vertical = أسفل الملفّات
    private GridLength _savedConsoleLen = new(240);

    public ContainerExplorerWindow(SshConnectionInfo info, string containerId, string displayName, bool sudo)
    {
        InitializeComponent();
        _containerId = containerId;
        _sudo = sudo;
        _info = info;
        FlowDirection = Loc.Flow;

        Title = $"{Loc.T("srv.explorer.title")} · {displayName}";
        HeaderText.Text = $"⬢ {displayName}   ({containerId})";
        ConsoleHereBtn.Content = Loc.T("srv.explorer.consoleHere");
        ViewerCopyBtn.Content = Loc.T("srv.explorer.copy");
        ViewerDownloadBtn.Content = Loc.T("srv.explorer.download");
        ViewerEditBtn.Content = Loc.T("srv.explorer.edit");
        ViewerSaveBtn.Content = Loc.T("srv.explorer.save");
        InputOk.Content = Loc.T("srv.ed.ok");
        InputCancel.Content = Loc.T("srv.ed.cancel");

        _browseConn = new SshNetConnection(info);
        _files = new ContainerFiles(_browseConn, containerId, sudo);

        ConsoleTabsBar.ItemsSource = _consoleTabs;
        ShellPicker.ItemsSource = new[] { "Bash", "Sh", Loc.T("srv.explorer.customShell") };
        ShellPicker.SelectedIndex = 0;
        AddConsole("");                    // أوّل تيرمنال (الصدفة الافتراضيّة)
        ApplyLayout();
        UpdateHeaders();                   // عناوين أعمدة القائمة قبل أوّل تحميل

        Loaded += (_, _) => _ = NavigateAsync("/");
    }

    // ===== إدارة تبويبات الكونسول =====

    /// <summary>الأمر الداخليّ (docker exec) للخيار المختار في منتقي الصدفة، أو أمر مخصّص مُدخَل.</summary>
    private async Task<string?> ResolveShellCommandAsync()
    {
        return ShellPicker.SelectedIndex switch
        {
            1 => "sh",
            2 => await PromptAsync(Loc.T("srv.explorer.customShell"), "bash").ConfigureAwait(true),
            _ => "",   // Bash الافتراضيّة
        };
    }

    private async void AddConsole_Click(object sender, RoutedEventArgs e)
    {
        string? shell = await ResolveShellCommandAsync().ConfigureAwait(true);
        if (shell is null) return;   // أُلغي الأمر المخصّص
        AddConsole(shell);
    }

    /// <summary>ينشئ تيرمنال حاوية جديداً (جلسة docker exec) ويضيفه كتبويب نشط.</summary>
    private void AddConsole(string innerShell)
    {
        string title;
        try
        {
            string remoteCmd = ContainerShell.BuildRemoteCommand(_containerId, _sudo,
                string.IsNullOrWhiteSpace(innerShell) ? null : innerShell);
            title = $"{Loc.T("srv.explorer.console")} {++_consoleSeq}";
            var shellDef = new global::TerminalLauncher.Terminal.ShellDef(
                Key: "container:" + _containerId + ":" + _consoleSeq, Display: title,
                CommandLine: "", Newline: "\n", Available: true);
            var entry = new CommandEntry { Name = title, Shell = "cmd", IsTransient = true };
            var view = new TerminalTabView(entry, static () => { }, ToggleMaximizeConsole,
                fontSize: 13, persistFontSize: null, aiEnabled: static () => false,
                sessionId: null, shellOverride: shellDef,
                sessionFactory: () => new SshShellSession(_info, remoteCmd));

            var vm = new ConsoleTabVm(title, view);
            view.CloseRequested += _ => CloseConsoleTab(vm);
            view.DetachRequested += _ => DetachTab(vm);
            _consoleTabs.Add(vm);
            ConsoleContent.Children.Add(view);
            ActivateConsole(vm);
            if (_consoleState == ConsoleState.Collapsed) SetConsoleState(ConsoleState.Split);
        }
        catch (Exception ex)
        {
            AppDialog.Alert(this, Loc.T("srv.explorer.console"), $"{Loc.T("srv.docker.enter")}: {ex.Message}");
        }
    }

    private void ActivateConsole(ConsoleTabVm vm)
    {
        _activeConsole = vm;
        foreach (var t in _consoleTabs)
        {
            t.IsActive = ReferenceEquals(t, vm);
            t.View.Visibility = t.IsActive ? Visibility.Visible : Visibility.Collapsed;
        }
        vm.View.FocusTerminal();
    }

    private void ConsoleTab_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ConsoleTabVm vm) ActivateConsole(vm);
    }

    private void ConsoleTabClose_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ConsoleTabVm vm) CloseConsoleTab(vm);
    }

    private void ConsoleTabCloseMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ConsoleTabVmOf(sender) is { } vm) CloseConsoleTab(vm);
    }

    private async void ConsoleTabRename_Click(object sender, RoutedEventArgs e)
    {
        if (ConsoleTabVmOf(sender) is not { } vm) return;
        string? name = await PromptAsync(Loc.T("srv.explorer.rename"), vm.Name).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(name)) vm.Name = name.Trim();
    }

    private static ConsoleTabVm? ConsoleTabVmOf(object sender)
        => sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement fe } }
            ? fe.DataContext as ConsoleTabVm
            : (sender as FrameworkElement)?.DataContext as ConsoleTabVm;

    private void CloseConsoleTab(ConsoleTabVm vm)
    {
        int idx = _consoleTabs.IndexOf(vm);
        if (idx < 0) return;
        _consoleTabs.Remove(vm);
        ConsoleContent.Children.Remove(vm.View);
        try { vm.View.CloseSession(deleteHistory: true); } catch { }

        if (_consoleTabs.Count == 0) { _activeConsole = null; SetConsoleState(ConsoleState.Collapsed); return; }
        ActivateConsole(_consoleTabs[Math.Min(idx, _consoleTabs.Count - 1)]);
    }

    // ===== التصفّح =====

    private async Task NavigateAsync(string path)
    {
        if (_busy) return;
        _busy = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        string target = ContainerFiles.NormalizePath(path);
        PathBox.Text = target;
        Loading.Visibility = Visibility.Visible;
        EmptyBox.Visibility = Visibility.Collapsed;
        try
        {
            if (!_browseConn.IsConnected)
                await _browseConn.ConnectAsync(ct).ConfigureAwait(true);

            var entries = await _files.ListAsync(target, ct).ConfigureAwait(true);
            _path = target;
            _currentFiles = entries.Select(e => new ContainerFileVm(e, target)).ToList();
            ApplySort();

            if (entries.Count == 0)
            {
                EmptyText.Text = Loc.T("srv.explorer.empty");
                EmptyBox.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) { /* استُبدِل بملاحة أحدث */ }
        catch (Exception ex)
        {
            _currentFiles = new();
            FileList.ItemsSource = null;
            EmptyText.Text = $"{Loc.T("srv.file.opFail")} {ex.Message}";
            EmptyBox.Visibility = Visibility.Visible;
        }
        finally
        {
            Loading.Visibility = Visibility.Collapsed;
            _busy = false;
        }
    }

    private void OpenSelected()
    {
        if (FileList.SelectedItem is not ContainerFileVm vm) return;
        if (vm.IsDir) _ = NavigateAsync(vm.FullPath);
        else _ = ViewFileAsync(vm.FullPath);
    }

    // ===== الفرز بالنقر على ترويسة العمود =====

    /// <summary>نقر ترويسة عمود: يبدّل الاتّجاه إن كان العمود نفسه، أو يفرز تصاعديّاً بعمود جديد.</summary>
    private void SortHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tag
            || !Enum.TryParse<SortKey>(tag, out var key)) return;
        if (key == _sortKey) _sortAsc = !_sortAsc;
        else { _sortKey = key; _sortAsc = true; }
        ApplySort();
    }

    /// <summary>يعيد فرز الملفّات الحاليّة (المجلّدات أوّلاً دائماً) ويحدّث مؤشّرات الترويسة.</summary>
    private void ApplySort()
    {
        Comparison<ContainerFileVm> by = _sortKey switch
        {
            SortKey.Type     => (a, b) => string.Compare(a.TypeText, b.TypeText, StringComparison.CurrentCultureIgnoreCase),
            SortKey.Size     => (a, b) => a.Size.CompareTo(b.Size),
            SortKey.Modified => (a, b) => string.Compare(a.Modified, b.Modified, StringComparison.Ordinal),
            _                => (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase),
        };
        var sorted = _currentFiles.ToList();
        sorted.Sort((a, b) =>
        {
            if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;   // المجلّدات أوّلاً دائماً
            int r = by(a, b);
            if (r == 0) r = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            return _sortAsc ? r : -r;
        });
        FileList.ItemsSource = sorted;
        UpdateHeaders();
    }

    /// <summary>يضبط عناوين الأعمدة المترجَمة ويُلحق سهم الفرز (▲/▼) بالعمود النشط.</summary>
    private void UpdateHeaders()
    {
        HdrName.Content     = HeaderCaption(SortKey.Name,     "srv.explorer.colName");
        HdrType.Content     = HeaderCaption(SortKey.Type,     "srv.explorer.colType");
        HdrSize.Content     = HeaderCaption(SortKey.Size,     "srv.explorer.colSize");
        HdrModified.Content = HeaderCaption(SortKey.Modified, "srv.explorer.colModified");
    }

    private string HeaderCaption(SortKey key, string locKey)
        => Loc.T(locKey) + (key == _sortKey ? (_sortAsc ? "  ▲" : "  ▼") : "");

    // ===== عارض محتوى الملفّ =====

    /// <summary>يقرأ محتوى ملفّ داخل الحاوية ويعرضه في لوحة عارضة (مع كشف الثنائيّ والاقتطاع).</summary>
    private async Task ViewFileAsync(string path)
    {
        _viewerPath = path;
        _viewerIsBinary = false;
        EndEdit();                         // نبدأ دائماً في وضع القراءة
        ViewerPath.Text = path;
        ViewerText.Text = "";
        ViewerNote.Text = "";
        ViewerLoading.Visibility = Visibility.Visible;
        FileViewerOverlay.Visibility = Visibility.Visible;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            if (!_browseConn.IsConnected)
                await _browseConn.ConnectAsync(ct).ConfigureAwait(true);

            string content = await _files.ReadFileAsync(path, ct).ConfigureAwait(true);
            if (ContainerFiles.LooksBinary(content))
            {
                _viewerIsBinary = true;
                ViewerText.Text = "";
                ViewerNote.Text = Loc.T("srv.explorer.view.binary");
            }
            else
            {
                ViewerText.Text = content;
                if (System.Text.Encoding.UTF8.GetByteCount(content) >= ContainerFiles.MaxViewBytes)
                    ViewerNote.Text = Loc.T("srv.explorer.view.truncated");
            }
        }
        catch (OperationCanceledException) { /* أُغلق/استُبدِل */ }
        catch (Exception ex)
        {
            ViewerText.Text = "";
            ViewerNote.Text = $"{Loc.T("srv.file.opFail")} {ex.Message}";
        }
        finally
        {
            ViewerLoading.Visibility = Visibility.Collapsed;
        }
    }

    private void ViewerClose_Click(object sender, RoutedEventArgs e) => CloseViewer();
    private void ViewerOverlay_MouseDown(object sender, MouseButtonEventArgs e) => CloseViewer();
    private void ViewerCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void CloseViewer()
    {
        EndEdit();
        FileViewerOverlay.Visibility = Visibility.Collapsed;
        ViewerText.Text = "";
    }

    private void ViewerCopy_Click(object sender, RoutedEventArgs e)
    {
        try { if (ViewerText.Text.Length > 0) Clipboard.SetText(ViewerText.Text); } catch { }
    }

    private void ViewerDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_viewerPath.Length > 0) _ = DownloadFileAsync(_viewerPath);
    }

    // ===== تعديل وحفظ الملفّ النصّي (المهمّة #14) =====

    private void ViewerEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_viewerIsBinary)
        {
            AppDialog.Alert(this, Loc.T("srv.explorer.open"), Loc.T("srv.explorer.view.binary"));
            return;
        }
        _editing = true;
        ViewerText.IsReadOnly = false;
        ViewerEditBtn.Visibility = Visibility.Collapsed;
        ViewerSaveBtn.Visibility = Visibility.Visible;
        ViewerText.Focus();
    }

    private void EndEdit()
    {
        _editing = false;
        ViewerText.IsReadOnly = true;
        ViewerEditBtn.Visibility = Visibility.Visible;
        ViewerSaveBtn.Visibility = Visibility.Collapsed;
    }

    private async void ViewerSave_Click(object sender, RoutedEventArgs e)
    {
        if (!_editing || _viewerPath.Length == 0) return;
        string tmp = System.IO.Path.GetTempFileName();
        ViewerLoading.Visibility = Visibility.Visible;
        try
        {
            System.IO.File.WriteAllText(tmp, ViewerText.Text, new System.Text.UTF8Encoding(false));
            await UploadLocalToContainerAsync(tmp, _viewerPath).ConfigureAwait(true);
            EndEdit();
            ViewerNote.Text = Loc.T("srv.explorer.saved");
        }
        catch (Exception ex)
        {
            AppDialog.Alert(this, Loc.T("srv.explorer.save"), $"{Loc.T("srv.file.opFail")} {ex.Message}");
        }
        finally
        {
            try { System.IO.File.Delete(tmp); } catch { }
            ViewerLoading.Visibility = Visibility.Collapsed;
        }
    }

    // ===== قائمة السياق: فتح/تنزيل/تعديل/تسمية/حذف/الكونسول هنا =====

    /// <summary>يستخرج نموذج المدخل من عنصر قائمة سياق موثوقاً عبر PlacementTarget (DataContext غير موثوق لـ ContextMenu داخل قالب).</summary>
    private static ContainerFileVm? MenuVm(object sender)
        => sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement fe } }
            ? fe.DataContext as ContainerFileVm
            : (sender as FrameworkElement)?.DataContext as ContainerFileVm;

    private void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        if (MenuVm(sender) is { } vm) { if (vm.IsDir) _ = NavigateAsync(vm.FullPath); else _ = ViewFileAsync(vm.FullPath); }
    }

    private async void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (MenuVm(sender) is { IsFile: true } vm)
        {
            await ViewFileAsync(vm.FullPath).ConfigureAwait(true);
            ViewerEdit_Click(sender, e);
        }
    }

    private void MenuDownload_Click(object sender, RoutedEventArgs e)
    {
        if (MenuVm(sender) is { IsFile: true } vm) _ = DownloadFileAsync(vm.FullPath);
    }

    private void MenuConsoleHere_Click(object sender, RoutedEventArgs e)
    {
        if (MenuVm(sender) is { IsDir: true } vm) SendCd(vm.FullPath);
    }

    private async void MenuRename_Click(object sender, RoutedEventArgs e)
    {
        if (MenuVm(sender) is not { } vm) return;
        string? name = await PromptAsync(Loc.T("srv.explorer.rename"), vm.Name).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || name == vm.Name) return;
        string dest = ContainerFiles.Join(ContainerFiles.Parent(vm.FullPath), name.Trim());
        await RunFileOpAsync(() => _files.RenameAsync(vm.FullPath, dest)).ConfigureAwait(true);
    }

    private async void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (MenuVm(sender) is not { } vm) return;
        string msg = vm.IsDir ? $"{Loc.T("srv.explorer.delDirConfirm")}\n{vm.FullPath}"
                              : $"{Loc.T("srv.explorer.delConfirm")}\n{vm.FullPath}";
        if (AppDialog.Confirm(this, Loc.T("srv.explorer.delete"), msg,
                (Loc.T("srv.ed.cancel"), "cancel", DialogButtonKind.Neutral),
                (Loc.T("srv.explorer.delete"), "del", DialogButtonKind.Danger)) != "del")
            return;
        await RunFileOpAsync(() => _files.DeleteAsync(vm.FullPath, vm.IsDir)).ConfigureAwait(true);
    }

    // ===== شريط الأدوات: مجلّد جديد + رفع =====

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        string? name = await PromptAsync(Loc.T("srv.explorer.newFolder"), "").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;
        string dest = ContainerFiles.Join(_path, name.Trim());
        await RunFileOpAsync(() => _files.MakeDirectoryAsync(dest)).ConfigureAwait(true);
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("srv.explorer.upload"), Filter = "All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;

        string dest = ContainerFiles.Join(_path, System.IO.Path.GetFileName(dlg.FileName));
        Loading.Visibility = Visibility.Visible;
        try
        {
            await UploadLocalToContainerAsync(dlg.FileName, dest).ConfigureAwait(true);
            await NavigateAsync(_path).ConfigureAwait(true);
            ViewerNote.Text = "";
        }
        catch (Exception ex)
        {
            AppDialog.Alert(this, Loc.T("srv.explorer.upload"), $"{Loc.T("srv.file.opFail")} {ex.Message}");
        }
        finally { Loading.Visibility = Visibility.Collapsed; }
    }

    /// <summary>ينفّذ عمليّة ملفّ (حذف/تسمية/mkdir) ثمّ يحدّث القائمة؛ يعرض الخطأ إن فشلت.</summary>
    private async Task RunFileOpAsync(Func<Task> op)
    {
        Loading.Visibility = Visibility.Visible;
        try { await op().ConfigureAwait(true); await NavigateAsync(_path).ConfigureAwait(true); }
        catch (Exception ex) { AppDialog.Alert(this, Loc.T("srv.explorer.title"), $"{Loc.T("srv.file.opFail")} {ex.Message}"); }
        finally { Loading.Visibility = Visibility.Collapsed; }
    }

    /// <summary>
    /// يرفع ملفّاً محلّيّاً إلى داخل الحاوية: SFTP إلى مؤقّت على المضيف → <c>docker cp</c> إلى المسار
    /// داخل الحاوية → حذف المؤقّت. آمن للبيانات الثنائيّة وأيّ حجم.
    /// </summary>
    private async Task UploadLocalToContainerAsync(string localPath, string destPath)
    {
        string hostTmp = "/tmp/tl-upload-" + Guid.NewGuid().ToString("N");
        using (var sftp = new SshNetSftp(_info))
        {
            await sftp.ConnectAsync().ConfigureAwait(false);
            await sftp.UploadAsync(localPath, hostTmp).ConfigureAwait(false);
        }
        try
        {
            await _files.CopyInAsync(hostTmp, destPath).ConfigureAwait(false);
        }
        finally
        {
            // تنظيف المؤقّت على المضيف (أفضل جهد).
            try { await _browseConn.RunAsync("rm -f -- '" + hostTmp + "'").ConfigureAwait(false); } catch { }
        }
    }

    /// <summary>
    /// ينزّل ملفّاً من داخل الحاوية: يسأل عن مكان الحفظ (الافتراضيّ مجلّد التنزيلات) ثمّ يبثّ
    /// <c>docker exec … cat</c> خاماً إلى الملفّ المحلّيّ (آمن للبيانات الثنائيّة).
    /// </summary>
    private async Task DownloadFileAsync(string path)
    {
        int slash = path.LastIndexOf('/');
        string name = slash >= 0 ? path[(slash + 1)..] : path;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.T("srv.explorer.download"),
            FileName = name,
            Filter = "All files|*.*",
        };
        string downloads = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (System.IO.Directory.Exists(downloads)) dlg.InitialDirectory = downloads;

        if (dlg.ShowDialog(this) != true) return;

        Loading.Visibility = Visibility.Visible;
        try
        {
            if (!_browseConn.IsConnected)
                await _browseConn.ConnectAsync().ConfigureAwait(true);

            string cmd = ContainerFiles.BuildCat(_containerId, path, _sudo);
            int exit;
            using (var fs = System.IO.File.Create(dlg.FileName))
                exit = await _browseConn.DownloadToStreamAsync(cmd, fs).ConfigureAwait(true);

            if (exit != 0)
            {
                try { System.IO.File.Delete(dlg.FileName); } catch { }
                throw new InvalidOperationException($"docker exec cat → رمز {exit}");
            }
            AppDialog.Alert(this, Loc.T("srv.explorer.download"), Loc.T("srv.file.downloadOk"));
        }
        catch (Exception ex)
        {
            AppDialog.Alert(this, Loc.T("srv.explorer.download"), $"{Loc.T("srv.file.downloadFail")} {ex.Message}");
        }
        finally
        {
            Loading.Visibility = Visibility.Collapsed;
        }
    }

    // ===== تخطيط الكونسول: إظهار/إخفاء + تكبير + اتّجاه =====

    private void ConsoleToggle_Click(object sender, RoutedEventArgs e)
    {
        bool show = ConsoleToggle.IsChecked == true;
        if (show && _consoleTabs.Count == 0) AddConsole("");   // أوّل تيرمنال عند إعادة الفتح فارغاً
        SetConsoleState(show ? ConsoleState.Split : ConsoleState.Collapsed);
    }

    private void Orientation_Click(object sender, RoutedEventArgs e)
    {
        if (_consoleState == ConsoleState.Split) SaveConsoleLen();
        _consoleOrientation = _consoleOrientation == Orientation.Vertical
            ? Orientation.Horizontal : Orientation.Vertical;
        ApplyLayout();
    }

    /// <summary>callback زرّ التوسيع في هدر التيرمنال: يبدّل بين تكبير الكونسول والوضع المقسوم. يعيد هل كُبِّر.</summary>
    private bool ToggleMaximizeConsole()
    {
        SetConsoleState(_consoleState == ConsoleState.Maximized ? ConsoleState.Split : ConsoleState.Maximized);
        return _consoleState == ConsoleState.Maximized;
    }

    private void SetConsoleState(ConsoleState state)
    {
        if (_consoleState == ConsoleState.Split && state != ConsoleState.Split) SaveConsoleLen();
        _consoleState = state;
        ApplyLayout();
    }

    /// <summary>يحفظ طول الكونسول الحاليّ (بعد سحب الفاصل) لاستعادته عند العودة للوضع المقسوم.</summary>
    private void SaveConsoleLen()
    {
        if (_consoleOrientation == Orientation.Vertical && SplitHost.RowDefinitions.Count == 3)
        {
            double h = SplitHost.RowDefinitions[2].ActualHeight;
            if (h > 20) _savedConsoleLen = new GridLength(h);
        }
        else if (SplitHost.ColumnDefinitions.Count == 3)
        {
            double w = SplitHost.ColumnDefinitions[2].ActualWidth;
            if (w > 20) _savedConsoleLen = new GridLength(w);
        }
    }

    /// <summary>يعيد بناء تعريفات SplitHost (صفوف/أعمدة) حسب الاتّجاه وحالة الكونسول.</summary>
    private void ApplyLayout()
    {
        bool vert = _consoleOrientation == Orientation.Vertical;
        bool showConsole = _consoleState != ConsoleState.Collapsed;
        bool showFiles = _consoleState != ConsoleState.Maximized;

        FileArea.Visibility = showFiles ? Visibility.Visible : Visibility.Collapsed;
        ConsoleHost.Visibility = showConsole ? Visibility.Visible : Visibility.Collapsed;
        ConsoleSplitter.Visibility = (showConsole && showFiles) ? Visibility.Visible : Visibility.Collapsed;
        ConsoleToggle.IsChecked = showConsole;
        OrientationBtn.Content = vert ? "⬍" : "⬌";   // ⬍ أسفل · ⬌ جانب

        GridLength fileLen = showFiles ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        GridLength consoleLen =
            !showConsole ? new GridLength(0)
            : _consoleState == ConsoleState.Maximized ? new GridLength(1, GridUnitType.Star)
            : _savedConsoleLen;
        double consoleMin = (showConsole && _consoleState == ConsoleState.Split) ? (vert ? 70 : 120) : 0;
        double fileMin = showFiles ? (vert ? 80 : 120) : 0;

        SplitHost.RowDefinitions.Clear();
        SplitHost.ColumnDefinitions.Clear();

        if (vert)
        {
            SplitHost.RowDefinitions.Add(new RowDefinition { Height = fileLen, MinHeight = fileMin });
            SplitHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SplitHost.RowDefinitions.Add(new RowDefinition { Height = consoleLen, MinHeight = consoleMin });
            Grid.SetRow(FileArea, 0); Grid.SetColumn(FileArea, 0);
            Grid.SetRow(ConsoleSplitter, 1); Grid.SetColumn(ConsoleSplitter, 0);
            Grid.SetRow(ConsoleHost, 2); Grid.SetColumn(ConsoleHost, 0);
            ConsoleSplitter.ResizeDirection = GridResizeDirection.Rows;
            ConsoleSplitter.Height = 6; ConsoleSplitter.Width = double.NaN;
            ConsoleSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            ConsoleSplitter.VerticalAlignment = VerticalAlignment.Center;
            ConsoleHost.BorderThickness = new Thickness(0, 1, 0, 0);
        }
        else
        {
            SplitHost.ColumnDefinitions.Add(new ColumnDefinition { Width = fileLen, MinWidth = fileMin });
            SplitHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SplitHost.ColumnDefinitions.Add(new ColumnDefinition { Width = consoleLen, MinWidth = consoleMin });
            Grid.SetColumn(FileArea, 0); Grid.SetRow(FileArea, 0);
            Grid.SetColumn(ConsoleSplitter, 1); Grid.SetRow(ConsoleSplitter, 0);
            Grid.SetColumn(ConsoleHost, 2); Grid.SetRow(ConsoleHost, 0);
            ConsoleSplitter.ResizeDirection = GridResizeDirection.Columns;
            ConsoleSplitter.Width = 6; ConsoleSplitter.Height = double.NaN;
            ConsoleSplitter.VerticalAlignment = VerticalAlignment.Stretch;
            ConsoleSplitter.HorizontalAlignment = HorizontalAlignment.Center;
            ConsoleHost.BorderThickness = new Thickness(1, 0, 0, 0);
        }
    }

    // ===== فصل تبويب كونسول لنافذة مستقلّة + إرجاعه =====

    private bool _isClosing;

    /// <summary>يفصل تبويباً لنافذة مستقلّة (يحافظ على جلسته الحيّة)؛ إغلاق النافذة يعيد إرساءه كتبويب.</summary>
    private void DetachTab(ConsoleTabVm vm)
    {
        if (!_consoleTabs.Contains(vm)) return;
        _consoleTabs.Remove(vm);
        ConsoleContent.Children.Remove(vm.View);
        if (ReferenceEquals(_activeConsole, vm))
        {
            _activeConsole = null;
            if (_consoleTabs.Count > 0) ActivateConsole(_consoleTabs[0]);
            else SetConsoleState(ConsoleState.Collapsed);
        }

        var host = new Grid();
        vm.View.Visibility = Visibility.Visible;
        host.Children.Add(vm.View);
        var redock = new Button
        {
            Content = Loc.T("srv.explorer.redock"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8), Padding = new Thickness(12, 5, 12, 5),
        };
        DockPanel.SetDock(redock, Dock.Top);
        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(redock);
        root.Children.Add(host);

        var win = new Window
        {
            Title = $"{vm.Name} · {_containerId}",
            Width = 820, Height = 480, MinWidth = 420, MinHeight = 240,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FlowDirection = Loc.Flow, Content = root,
        };
        win.SetResourceReference(BackgroundProperty, "Brush.Bg");
        redock.Click += (_, _) => win.Close();
        win.Closed += (_, _) => ReDockTab(vm);
        win.Show();
    }

    private void ReDockTab(ConsoleTabVm vm)
    {
        if (_isClosing) return;
        DetachFromParent(vm.View);
        _consoleTabs.Add(vm);
        ConsoleContent.Children.Add(vm.View);
        ActivateConsole(vm);
        if (_consoleState == ConsoleState.Collapsed) SetConsoleState(ConsoleState.Split);
    }

    private static void DetachFromParent(System.Windows.FrameworkElement el)
    {
        switch (el.Parent)
        {
            case Panel p: p.Children.Remove(el); break;
            case System.Windows.Controls.Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
        }
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
    }

    private void Up_Click(object sender, RoutedEventArgs e) => _ = NavigateAsync(ContainerFiles.Parent(_path));
    private void Home_Click(object sender, RoutedEventArgs e) => _ = NavigateAsync("/");
    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = NavigateAsync(_path);

    private void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _ = NavigateAsync(PathBox.Text?.Trim() ?? "/"); e.Handled = true; }
    }

    /// <summary>يرسل <c>cd</c> إلى الكونسول لينتقل للمسار المعروض في المستكشف.</summary>
    private void ConsoleHere_Click(object sender, RoutedEventArgs e) => SendCd(_path);

    /// <summary>يُظهر الكونسول (إن كان مطويّاً) ويرسل للتبويب النشط <c>cd</c> للمسار المعطى.</summary>
    private void SendCd(string path)
    {
        if (_consoleState == ConsoleState.Collapsed) SetConsoleState(ConsoleState.Split);
        if (_consoleTabs.Count == 0) AddConsole("");
        var view = ActiveConsoleView;
        if (view is null) return;
        string quoted = "'" + path.Replace("'", "'\\''") + "'";
        view.RunCommand("cd " + quoted);
        view.FocusTerminal();
    }

    // ===== لوحة إدخال نصّ (إعادة تسمية / مجلّد جديد) =====

    private TaskCompletionSource<string?>? _inputTcs;

    /// <summary>يعرض لوحة إدخال نصّ ويعيد النصّ عند «حسناً» أو null عند الإلغاء.</summary>
    private Task<string?> PromptAsync(string title, string initial)
    {
        InputTitle.Text = title;
        InputBox.Text = initial;
        InputOverlay.Visibility = Visibility.Visible;
        InputBox.Focus();
        InputBox.SelectAll();
        _inputTcs?.TrySetResult(null);
        _inputTcs = new TaskCompletionSource<string?>();
        return _inputTcs.Task;
    }

    private void CloseInput(string? result)
    {
        InputOverlay.Visibility = Visibility.Collapsed;
        var tcs = _inputTcs;
        _inputTcs = null;
        tcs?.TrySetResult(result);
    }

    private void InputOk_Click(object sender, RoutedEventArgs e) => CloseInput(InputBox.Text);
    private void InputCancel_Click(object sender, RoutedEventArgs e) => CloseInput(null);
    private void InputOverlay_MouseDown(object sender, MouseButtonEventArgs e) => CloseInput(null);
    private void InputCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CloseInput(InputBox.Text); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseInput(null); e.Handled = true; }
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _cts?.Cancel();
        // إنهاء جلسات كلّ تبويبات الكونسول (تخلّص غير حاجب — خلفيّ داخل الجلسة).
        foreach (var t in _consoleTabs.ToList())
            try { t.View.CloseSession(deleteHistory: true); } catch { }
        // قطع اتّصال التصفّح على خيط خلفيّ كي لا يُجمّد الواجهة (مراقب الخوادم) لثوانٍ.
        var conn = _browseConn;
        System.Threading.Tasks.Task.Run(() => { try { conn.Dispose(); } catch { } });
        base.OnClosed(e);
    }
}

/// <summary>تبويب كونسول: اسم قابل للتغيير + حالة نشاط + العرض المستضيف لجلسة docker exec.</summary>
public sealed class ConsoleTabVm : INotifyPropertyChanged
{
    private string _name;
    private bool _isActive;

    public ConsoleTabVm(string name, TerminalTabView view) { _name = name; View = view; }

    public TerminalTabView View { get; }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(nameof(IsActive)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>صفّ في قائمة المستكشف: اسم + نوع + حجم + تاريخ + مسار + أيقونة.</summary>
public sealed class ContainerFileVm
{
    public string Name { get; }
    public bool IsDir { get; }
    public bool IsFile => !IsDir;
    public char Type { get; }
    public long Size { get; }
    public string Modified { get; }
    public string FullPath { get; }

    /// <summary>أيقونة Segoe MDL2: مجلّد (E8B7) · رابط رمزيّ (E71B) · مستند (E7C3).</summary>
    public string Glyph => System.Char.ConvertFromUtf32(IsDir ? 0xE8B7 : Type == 'l' ? 0xE71B : 0xE7C3);

    /// <summary>الحجم مُنسَّقاً (B/KB/MB/GB) للملفّات؛ فارغ للمجلّدات.</summary>
    public string SizeText => IsDir ? "" : FormatSize(Size);

    /// <summary>نوع المدخل نصّاً مترجَماً (مجلّد/رابط/فئة الامتداد) — يُستعمل للعرض وللفرز بعمود «النوع».</summary>
    public string TypeText
    {
        get
        {
            if (IsDir) return Loc.T("srv.type.folder");
            if (Type == 'l') return Loc.T("srv.type.link");
            int dot = Name.LastIndexOf('.');
            if (dot <= 0 || dot >= Name.Length - 1) return Loc.T("srv.type.file");
            string ext = Name[(dot + 1)..].ToLowerInvariant();
            return ExtTypeKeys.TryGetValue(ext, out var key) ? Loc.T(key) : ext.ToUpperInvariant();
        }
    }

    /// <summary>تصنيف امتدادات شائعة إلى مفتاح ترجمة فئة (غير المُدرَج يُعرَض بامتداده كبيراً).</summary>
    private static readonly Dictionary<string, string> ExtTypeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["txt"] = "srv.type.text", ["log"] = "srv.type.text", ["md"] = "srv.type.text",
        ["ini"] = "srv.type.text", ["cfg"] = "srv.type.text", ["conf"] = "srv.type.text",
        ["env"] = "srv.type.text", ["properties"] = "srv.type.text",
        ["json"] = "srv.type.data", ["xml"] = "srv.type.data", ["yaml"] = "srv.type.data",
        ["yml"] = "srv.type.data", ["toml"] = "srv.type.data", ["csv"] = "srv.type.data",
        ["sql"] = "srv.type.data", ["db"] = "srv.type.data",
        ["sh"] = "srv.type.code", ["bash"] = "srv.type.code", ["py"] = "srv.type.code",
        ["js"] = "srv.type.code", ["ts"] = "srv.type.code", ["php"] = "srv.type.code",
        ["rb"] = "srv.type.code", ["go"] = "srv.type.code", ["rs"] = "srv.type.code",
        ["c"] = "srv.type.code", ["cpp"] = "srv.type.code", ["h"] = "srv.type.code",
        ["java"] = "srv.type.code", ["cs"] = "srv.type.code",
        ["png"] = "srv.type.image", ["jpg"] = "srv.type.image", ["jpeg"] = "srv.type.image",
        ["gif"] = "srv.type.image", ["svg"] = "srv.type.image", ["webp"] = "srv.type.image",
        ["ico"] = "srv.type.image", ["bmp"] = "srv.type.image",
        ["zip"] = "srv.type.archive", ["tar"] = "srv.type.archive", ["gz"] = "srv.type.archive",
        ["tgz"] = "srv.type.archive", ["bz2"] = "srv.type.archive", ["xz"] = "srv.type.archive",
        ["7z"] = "srv.type.archive", ["rar"] = "srv.type.archive",
        ["mp4"] = "srv.type.video", ["mkv"] = "srv.type.video", ["avi"] = "srv.type.video",
        ["mov"] = "srv.type.video", ["webm"] = "srv.type.video",
        ["mp3"] = "srv.type.audio", ["wav"] = "srv.type.audio", ["flac"] = "srv.type.audio",
        ["ogg"] = "srv.type.audio",
        ["html"] = "srv.type.web", ["htm"] = "srv.type.web", ["css"] = "srv.type.web",
        ["pdf"] = "srv.type.doc", ["doc"] = "srv.type.doc", ["docx"] = "srv.type.doc",
        ["so"] = "srv.type.binary", ["bin"] = "srv.type.binary", ["exe"] = "srv.type.binary",
        ["dll"] = "srv.type.binary", ["o"] = "srv.type.binary", ["a"] = "srv.type.binary",
    };

    public ContainerFileVm(ContainerEntry e, string parentPath)
    {
        Name = e.Name;
        IsDir = e.IsDir;
        Type = e.Type;
        Size = e.Size;
        Modified = e.Modified;
        FullPath = ContainerFiles.Join(parentPath, e.Name);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 0) return "";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} {units[0]}" : $"{v:0.#} {units[u]}";
    }
}
