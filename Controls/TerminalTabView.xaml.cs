using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TerminalLauncher.Models;
using TerminalLauncher.Terminal;
using CoreMouseButton = Terminal.Core.Screen.MouseButton;
using CoreMouseEventType = Terminal.Core.Screen.MouseEventType;

namespace TerminalLauncher.Controls;

public partial class TerminalTabView : UserControl
{
    private const double MinFontSize = 8;
    private const double MaxFontSize = 32;
    private const double DefaultFontSize = 13;

    private readonly CommandEntry _entry;
    private readonly Action _persist;
    private readonly Func<bool> _toggleSidebar;
    private readonly Action<double>? _persistFontSize;
    private readonly Services.IAiAssistant _ai;

    private readonly object _screenLock = new();
    private readonly DispatcherTimer _refresh;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _blinkTimer;

    // حجم الخطّ الحاليّ (بدل قراءته من مربّع نصّ) — العارض يتبعه.
    private double _fontSize = DefaultFontSize;

    // آخر لقطة (لمعرفة حدود الكتل/الشاشة البديلة للتنقّل والنسخ).
    private ScreenSnapshot? _lastSnapshot;

    // سجلّ الأوامر المنفّذة (T-106): يُخزَّن في SQLite ويُستدعى من زرّ السجلّ في الشريط.
    private readonly global::Terminal.Storage.CommandHistoryStore _history =
        new(new global::Terminal.Storage.AppDatabase());
    // آخر نصّ أمر سُجِّل من كتل OSC 133 — لمنع التكرار في حلقة التحديث (40ms).
    private string? _lastRecordedBlockCommand;

    // ===== إشعار انتهاء الأوامر الطويلة (T-211) =====
    // نموذج كتل المحرّك بلا أختام زمنيّة، فنقيس المدّة هنا: مفتاح القاموس StartLine المطلق (هويّة ثابتة
    // للكتلة)، وقيمته ختمُ أوّل رؤية لها وهي تعمل + هل رأت شاشةً بديلة (تطبيق TUI) أثناء عملها.
    // الحذف عند الاكتمال يجعل الإشعار يُطلَق مرّةً واحدة لكلّ كتلة رغم حلقة التحديث (40ms).
    // الختم من Stopwatch (ساعة رتيبة) لا DateTime: مزامنة NTP أو تغيّر التوقيت الصيفيّ أثناء أمرٍ
    // طويل يقفز بساعة الحائط، فتخرج مدّة سالبة أو بساعةٍ زائدة.
    private readonly Dictionary<long, (long StartedAt, bool SawAlt)> _runningBlocks = new();

    /// <summary>عتبة «الأمر الطويل»: ما دونها لا يستحقّ إشعاراً.</summary>
    private static readonly TimeSpan LongCommandThreshold = TimeSpan.FromSeconds(30);

    /// <summary>أقصى طول لنصّ الأمر داخل الإشعار (يُقصّ بعدها بعلامة حذف).</summary>
    private const int NotifyCommandMaxLength = 60;

    // ===== تاريخ الجلسة (خاصّ بهذا التيرمنال) =====
    // قائمة أوامر هذه الجلسة بالترتيب (للتنقّل بالأسهم + إعادة تنفيذ آخر أمر عند الاسترجاع). تُخزَّن في
    // SQLite بمفتاح SessionId فتبقى بين التشغيلات، وتُحذَف عند إغلاق المستخدم للجلسة.
    private readonly global::Terminal.Storage.SessionHistoryStore _sessionHistory =
        new(new global::Terminal.Storage.AppDatabase());
    private readonly List<string> _sessionCommands = new();
    private int _histIndex = -1;          // موضع التنقّل في _sessionCommands (-1 = عند السطر الحيّ)
    private string _histSavedLine = "";   // السطر الحيّ المحفوظ قبل بدء التنقّل

    /// <summary>معرّف الجلسة (يربطها بتاريخها المخزَّن)؛ يُعاد استعماله عند الاسترجاع.</summary>
    public string SessionId { get; }

    /// <summary>آخر أمر نُفِّذ في هذه الجلسة (يُعاد تنفيذه عند استرجاع الجلسة)، أو null.</summary>
    public string? LastCommand => _sessionCommands.Count > 0 ? _sessionCommands[^1] : null;

    // ===== الإكمال التلقائيّ الشبحيّ (T-205): تتبّع تقريبيّ لسطر الإدخال الحاليّ =====
    // التيرمنال خام (الصدفة تملك تحرير السطر)، فنتتبّع الإدخال محلّياً ونكون محافظين:
    // أيّ غموض في الحالة يمسح السطر والشبح (شبحٌ خاطئ أسوأ من لا شبح).
    private readonly StringBuilder _inputLine = new();

    // ===== صندوق التأليف (نمط Warp — الخيار B) =====
    // صندوق WPF منفصل يُكتب فيه الأمر ثمّ يُرسَل عند Enter. يظهر على الشاشة الأساس فقط ويختفي في
    // الشاشة البديلة (vim/less) — فلا يكسر التطبيقات كاملة الشاشة. راجع docs/WarpInputBox_Design.md.
    private bool _composerEnabled = true;
    private bool _composerSuppressReshow;   // Esc: يُبقيه مخفيّاً حتّى نقرة/تركيز يدويّ ولو بقينا بالشاشة الأساس

    /// <summary>تفعيل صندوق التأليف المنفصل (يضبطه المُضيف من الإعدادات). إطفاؤه يعيد الكتابة داخل الشبكة.</summary>
    public bool ComposerEnabled
    {
        get => _composerEnabled;
        set { _composerEnabled = value; UpdateComposerVisibility(); }
    }

    private string _newline = "\r";
    private int _commandSent;
    // يُكتَب من خيط قراءة الـPTY ويُستهلَك على خيط الواجهة ⇒ int عبر Interlocked لا bool:
    // النمط القديم (‏if (!_dirty) return; _dirty = false;‎) غير ذرّيّ، فكان تعيينٌ متزامنٌ من خيط
    // الـPTY يُطمَس بين القراءة والمسح ⇒ يسقط آخر إطار ويبقى حرف شبح على الشاشة.
    private int _dirty;
    private int _flushQueued;
    private long _lastFlushTs;                      // ختم آخر رسمة (ساعة رتيبة) لسقف الإطارات
    private const double MinFrameMs = 1000.0 / 60;  // سقف ~60 إطار/ث للإخراج الغزير
    private bool _started;
    private bool _initializing = true;

    private global::Terminal.Core.Pty.IPtySession? _coreSession;
    private global::Terminal.Core.Screen.ScreenBuffer? _coreScreen;

    // تجاوز صدفة لحظيّ (شِل حاوية): إن كان غير null تُشغَّل الجلسة به بدل كتالوج الصدفات (يوفّر نهاية
    // السطر)، ويُخفى كومبو الصدفة (التبديل يكسر الجلسة). _sessionFactory يبني الـ backend (SSH بدل ConPTY).
    private readonly global::TerminalLauncher.Terminal.ShellDef? _shellOverride;
    private readonly Func<global::Terminal.Core.Pty.IPtySession>? _sessionFactory;

    // حالة التشغيل (للحالة النصّية: PID / المدّة / رمز الخروج)
    private DateTime _startTime;
    private DateTime? _endTime;
    private int _pid;

    // ===== التحديد بالماوس (نمط الكونسول) =====
    private (int Line, int Col)? _selAnchor;
    private bool _selecting;

    // ===== شريط التمرير — حارس ضدّ حلقات التغذية الراجعة =====
    private bool _syncingScroll;

    // ===== حالة البحث (مطابقات بإحداثيّات فهارس snapshot.Lines) =====
    private List<(int Line, int StartCol, int Length)> _matches = new();
    private int _matchIndex = -1;

    // ===== اختصارات التطبيق المحجوزة (T-006.5) =====
    private enum TermAction { Copy, Paste, Search, BlockPrev, BlockNext, CopyBlock, ZoomIn, ZoomOut, ZoomReset, Close }
    private static readonly Dictionary<(Key key, ModifierKeys mods), TermAction> AppShortcuts = BuildShortcuts();

    /// <summary>مفتاح الصدفة الحاليّة — يستعمله الزرّ «تيرمنال جديد» كافتراضٍ ملائم.</summary>
    public string CurrentShellKey => _entry.Shell;

    /// <summary>يُطلَق حين يطلب المستخدم إغلاق هذا الجزء (زرّ ✕ في شريط الأدوات)؛ الحاوية تتولّى الإغلاق.</summary>
    public event Action<TerminalTabView>? CloseRequested;

    /// <summary>يُطلَق حين يطلب المستخدم فصل هذا التيرمنال إلى نافذة مستقلّة (زرّ الفصل، نقرة عاديّة).</summary>
    public event Action<TerminalTabView>? DetachRequested;

    public TerminalTabView(CommandEntry entry, Action persist, Func<bool> toggleSidebar,
        double fontSize = 13, Action<double>? persistFontSize = null, Func<bool>? aiEnabled = null,
        string? sessionId = null, global::TerminalLauncher.Terminal.ShellDef? shellOverride = null,
        Func<global::Terminal.Core.Pty.IPtySession>? sessionFactory = null)
    {
        InitializeComponent();
        _entry = entry;
        _persist = persist;
        _toggleSidebar = toggleSidebar;
        _persistFontSize = persistFontSize;
        _shellOverride = shellOverride;
        _sessionFactory = sessionFactory;
        _ai = new Services.AiAssistant(aiEnabled ?? (static () => false));

        // معرّف الجلسة: مُمرَّر عند الاسترجاع (يربط بالتاريخ المخزَّن) أو جديد للجلسة الجديدة.
        SessionId = string.IsNullOrEmpty(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
        try { _sessionCommands.AddRange(_sessionHistory.List(SessionId)); } catch { }

        ShellCombo.ItemsSource = ShellCatalog.All;
        ShellCombo.SelectedItem = ShellCatalog.Get(_entry.Shell);
        // شِل حاوية عبر ssh: نُخفي الكومبو — تبديل الصدفة يشغّل StartSession بكتالوج الصدفات فيكسر جلسة ssh.
        if (_shellOverride != null) ShellCombo.Visibility = Visibility.Collapsed;
        _initializing = false;

        HistoryButton.ToolTip = Services.Loc.T("tip.history");   // تلميح مُعرَّب (T-106)

        _fontSize = Math.Clamp(fontSize, MinFontSize, MaxFontSize);
        Renderer.TerminalFontSize = _fontSize;
        Renderer.TerminalFontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");

        // شبكة أمان فقط: الرسم الفعليّ صار فوريّاً عبر MarkDirty/RequestFlush عند وصول البيانات.
        // يبقى المؤقّت ليلتقط أيّ تعليمٍ للاتّساخ لم يُرافقه طلبُ رسم (فلا تتجمّد الشاشة أبداً).
        _refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _refresh.Tick += (_, _) => FlushOutput();

        _statusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => RefreshRunningStatus();

        // وميض المؤشّر (T-005.5): يقلّب طور الوميض كل ~530ms (نمط xterm). يخدم الشبكة ومؤشّر الصندوق.
        _blinkTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(530) };
        _blinkTimer.Tick += (_, _) =>
        {
            Renderer.CursorBlinkOn = !Renderer.CursorBlinkOn;
            if (ComposerCaret.Visibility == Visibility.Visible)
                ComposerCaret.Opacity = ComposerCaret.Opacity > 0.5 ? 0.0 : 1.0;
        };

        Loaded += OnLoaded;
        SizeChanged += (_, _) => OnViewSizeChanged();

        // لوحة تحرير الملفّ (T-7): عند طلبها الإغلاق نطوي عمود المحرّر ونعيد التيرمنال لكامل العرض.
        EditorPanel.CloseRequested += CollapseEditor;
    }

    /// <summary>يبني جدول الاختصارات المحجوزة للتطبيق (تُفحَص أوّلاً في PreviewKeyDown).</summary>
    private static Dictionary<(Key, ModifierKeys), TermAction> BuildShortcuts()
    {
        const ModifierKeys C = ModifierKeys.Control;
        const ModifierKeys CS = ModifierKeys.Control | ModifierKeys.Shift;
        return new Dictionary<(Key, ModifierKeys), TermAction>
        {
            { (Key.F, C), TermAction.Search },
            { (Key.Up, C), TermAction.BlockPrev },
            { (Key.Down, C), TermAction.BlockNext },
            { (Key.C, CS), TermAction.CopyBlock },
            { (Key.OemPlus, C), TermAction.ZoomIn },
            { (Key.Add, C), TermAction.ZoomIn },
            { (Key.OemMinus, C), TermAction.ZoomOut },
            { (Key.Subtract, C), TermAction.ZoomOut },
            { (Key.D0, C), TermAction.ZoomReset },
            { (Key.NumPad0, C), TermAction.ZoomReset },
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => TryStartSession();

    /// <summary>يبدأ الجلسة أو يعيد قياسها عند تغيّر حجم العرض (يبدؤها متأخّراً إن لم يكن الحجم جاهزاً بعد).</summary>
    private void OnViewSizeChanged()
    {
        if (!_started) TryStartSession();
        else ResizeSession();
    }

    /// <summary>
    /// يبدأ الجلسة مرّة واحدة، لكن فقط بعد أن يكتمل تخطيط العرض بحجم صالح. البدء قبل التخطيط (تبويب مخفيّ
    /// أو تخطيط مؤجَّل) يقيس أعمدة/أسطر مصغّرة (حدّ أدنى 20×5) فتُطلَق الصدفة بعرض ضيّق ويبقى بانرها/مخرجها
    /// الأوّل ملفوفاً حتى لو أُعيد القياس لاحقاً. لذا ننتظر أوّل حجم صالح (يصله حدث <c>SizeChanged</c>).
    /// </summary>
    private void TryStartSession()
    {
        if (_started || !HasRenderableSize()) return;
        _started = true;
        StartSession();
        _refresh.Start();
        _blinkTimer.Start();
        Renderer.Focus();
    }

    /// <summary>هل اكتمل تخطيط العرض بحجم صالح (لا صفر) يسمح بقياس أعمدة/أسطر حقيقيّة؟</summary>
    private bool HasRenderableSize()
    {
        double w = Renderer.ActualWidth > 0 ? Renderer.ActualWidth : ActualWidth;
        double h = Renderer.ActualHeight > 0 ? Renderer.ActualHeight : ActualHeight;
        return w >= 40 && h >= 24;
    }

    // ===== الجلسة =====

    private (short cols, short rows) Measure()
    {
        double w = Renderer.ActualWidth > 0 ? Renderer.ActualWidth : ActualWidth;
        double h = Renderer.ActualHeight > 0 ? Renderer.ActualHeight : ActualHeight;
        var (c, r) = Renderer.Measure(w, h);
        short cols = (short)Math.Max(20, Math.Min(500, c));
        short rows = (short)Math.Max(5, Math.Min(200, r));
        return (cols, rows);
    }

    private void StartSession()
    {
        if (_coreSession != null)
        {
            _coreSession.DataReceived -= OnCoreData;
            _coreSession.Exited -= OnExited;
            _coreSession.Dispose();
            _coreSession = null;
        }
        ClearDocument();

        var shell = _shellOverride ?? ShellCatalog.Get(_entry.Shell);
        _newline = shell.Newline;
        Interlocked.Exchange(ref _commandSent, 0);

        var (cols, rows) = Measure();
        try
        {
            lock (_screenLock) _coreScreen = new global::Terminal.Core.Screen.ScreenBuffer(cols, rows);
            _coreSession = _sessionFactory?.Invoke() ?? new global::Terminal.Core.Pty.PtySession();
            _coreSession.DataReceived += OnCoreData;
            _coreSession.Exited += OnExited;
            // مجلّد العمل: بروفايل الصدفة يتجاوز مسار الأمر المحفوظ إن حُدِّد (T-101.5).
            string workDir = NormalizeWorkDir(!string.IsNullOrWhiteSpace(shell.WorkingDirectory)
                ? shell.WorkingDirectory! : (_entry.Path ?? ""));
            // متغيّرات البيئة: تُضبَط على بيئة العمليّة قبل الإطلاق (يرثها ابن ConPTY) ثم تُستعاد.
            using (ApplyProfileEnvironment(shell.EnvironmentVariables))
                _coreSession.Start(shell.CommandLine, workDir, cols, rows);
            _pid = _coreSession.ProcessId;
            _startTime = DateTime.Now;
            _endTime = null;
            _statusTimer.Start();
            RefreshRunningStatus();
        }
        catch (Exception ex)
        {
            string msg = $"تعذّر بدء الجلسة: {ex.Message}\r\n";
            lock (_screenLock) _coreScreen?.FeedString(msg);
            MarkDirty();
            _statusTimer.Stop();
            SetStatus("خطأ", (Brush)FindResource("Brush.Danger"));
        }
    }

    /// <summary>
    /// يضبط متغيّرات بيئة البروفايل على بيئة العمليّة الحاليّة مؤقّتاً (T-101.5): ابن ConPTY
    /// المُطلَق بـ lpEnvironment=NULL يرث بيئة الأب، فنَضبطها قُبيل الإطلاق ونستعيدها بعده عبر
    /// كائن IDisposable (يُتخلَّص منه فور عودة Start؛ الإطلاق متزامن على خيط الواجهة).
    /// </summary>
    private static System.IDisposable ApplyProfileEnvironment(IReadOnlyDictionary<string, string>? vars)
    {
        if (vars == null || vars.Count == 0) return EmptyScope.Instance;
        var previous = new Dictionary<string, string?>();
        foreach (var kv in vars)
        {
            previous[kv.Key] = Environment.GetEnvironmentVariable(kv.Key);
            Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        }
        return new EnvironmentScope(previous);
    }

    /// <summary>نطاق يستعيد متغيّرات البيئة السابقة عند التخلّص (Dispose).</summary>
    private sealed class EnvironmentScope(Dictionary<string, string?> previous) : System.IDisposable
    {
        public void Dispose()
        {
            foreach (var kv in previous)
                Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        }
    }

    /// <summary>نطاق فارغ (لا متغيّرات بيئة) — يتجنّب تخصيصاً في المسار الشائع.</summary>
    private sealed class EmptyScope : System.IDisposable
    {
        public static readonly EmptyScope Instance = new();
        public void Dispose() { }
    }

    /// <summary>
    /// يُطبّع مجلّد العمل قبل تمريره لـ ConPTY: يوسّع متغيّرات البيئة (<c>%VAR%</c>)، يزيل علامات
    /// الاقتباس والفراغات، ويحوّل الشرطات المائلة الأماميّة إلى خلفيّة — كي لا يفشل مسارٌ صالح بصيغة مختلفة.
    /// (السبب الشائع لـ«تشغيل من فولدر معيّن لا يعمل»: مسار بمتغيّر بيئة أو باقتباس فيفشل <c>Directory.Exists</c>.)
    /// </summary>
    private static string NormalizeWorkDir(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string p = Environment.ExpandEnvironmentVariables(path.Trim()).Trim().Trim('"').Trim();
        if (p.Length == 0) return "";
        p = p.Replace('/', '\\');
        try { return System.IO.Directory.Exists(p) ? System.IO.Path.GetFullPath(p) : p; }
        catch { return p; }
    }

    /// <summary>بايتات خام من ConPTY → ScreenBuffer؛ يفتح كتلة استدلاليّة لأوّل أمر محفوظ.</summary>
    private void OnCoreData(byte[] data)
    {
        lock (_screenLock) _coreScreen?.Feed(data);
        MarkDirty();

        if (Interlocked.Exchange(ref _commandSent, 1) == 0)
        {
            // أمر المشروع قد يكون متعدّد الخطوات (سطر لكلّ خطوة) — تُنفَّذ بالتوالي.
            foreach (var raw in (_entry.Command ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
            {
                var step = raw.Trim();
                if (step.Length == 0) continue;
                lock (_screenLock) _coreScreen?.BeginHeuristicCommand(step);
                _coreSession?.Write(step + _newline);
                RecordHistory(step);   // التقاط الأمر المُنفَّذ (T-106)
            }
        }
    }

    /// <summary>يعلّم الشاشة متّسخة ويطلب رسمةً فوريّة (يُستدعى من خيط الـPTY ومن خيط الواجهة معاً).</summary>
    private void MarkDirty()
    {
        Interlocked.Exchange(ref _dirty, 1);
        RequestFlush();
    }

    /// <summary>
    /// يطلب رسمةً على خيط الواجهة <b>فور</b> وصول البيانات بدل انتظار مؤقّت ثابت.
    ///
    /// كان الرسم يعتمد على <c>_refresh</c> كلّ 40ms فقط ⇒ صدى كلّ حرف ينتظر حتى 40ms (وأكثر تحت الحِمل)،
    /// وتكرار Backspace (كلّ ~33ms) يتجمّع في إطار واحد فيبدو المسح «كلمة كلمة» لا حرفاً حرفاً.
    ///
    /// حارس <see cref="_flushQueued"/> يُبقي طلباً معلّقاً واحداً فقط، فيدمج الدفقات الغزيرة تلقائيّاً
    /// (SSH وتطبيقات ملء الشاشة) بلا سقف إطارات مصطنع: الحرف المفرد يظهر فوراً، والدفق الغزير
    /// يُرسَم بأسرع ما يستطيع خيط الواجهة. أولويّة <c>Input</c> (لا <c>Render</c>) كي لا تُزاحم
    /// الرسمُ معالجةَ ضغطات المفاتيح فتبقى الكتابة مستجيبة تحت الإخراج الكثيف.
    /// </summary>
    private void RequestFlush()
    {
        if (Interlocked.Exchange(ref _flushQueued, 1) == 1) return;   // طلب معلّق أصلاً
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            // يُصفَّر قبل الرسم عمداً: بياناتٌ تصل أثناء الرسم تُجدوِل تمريرةً تالية بدل أن تُهمَل.
            Interlocked.Exchange(ref _flushQueued, 0);

            // سقف 60 إطار/ث: إخراجٌ غزير (cat لملفّ كبير) كان سيرسم مئات الإطارات في الثانية بلا فائدة
            // مرئيّة. الخروج هنا لا يستهلك _dirty، فالبيانات التالية تُعيد الجدولة و«شبكة الأمان»
            // (_refresh) تلتقط الإطار الأخير إن توقّف الدفق فجأة. الحرف المفرد بعد سكون يمرّ فوراً.
            if (_lastFlushTs != 0 &&
                System.Diagnostics.Stopwatch.GetElapsedTime(_lastFlushTs).TotalMilliseconds < MinFrameMs)
                return;

            FlushOutput();
        }));
    }

    private void FlushOutput()
    {
        // استهلاك ذرّيّ: تعيينٌ متزامن من خيط الـPTY لا يمكن أن يضيع (وإلّا سقط آخر إطار).
        if (Interlocked.Exchange(ref _dirty, 0) == 0) return;
        _lastFlushTs = System.Diagnostics.Stopwatch.GetTimestamp();
        global::Terminal.Core.Screen.ScreenSnapshot? core;
        lock (_screenLock) core = _coreScreen?.Snapshot();
        if (core == null) return;
        ScreenSnapshot snap = CoreSnapshotAdapter.ToLauncher(core);
        _lastSnapshot = snap;

        CaptureCompletedBlockCommand(snap);    // التقاط أوامر كتل OSC 133 المكتملة (T-106)
        TrackLongCommandBlocks(snap);          // إشعار انتهاء الأوامر الطويلة (T-211)

        // التطبيق يملك سطره (شاشة بديلة أو لصق مُقوَّس) ⇒ تتبّعنا غير موثوق ⇒ نظّف السطر والشبح
        // فوراً (T-205). بلا هذا يبقى الشبح مرسوماً فوق واجهة التطبيق ولا يستطيع محوَه.
        if ((_inputLine.Length > 0 || Renderer.GhostText != null) && AppOwnsInputLine())
            ClearInputTracking();

        // تدفّق جديد يزيح فهارس الأسطر ⇒ نُنظّف تمييز البحث (يُعاد بناؤه عند الطلب).
        if (_matches.Count > 0)
        {
            _matches.Clear();
            _matchIndex = -1;
            Renderer.ClearSearchMatches();
            if (SearchBar.Visibility == Visibility.Visible) SearchCount.Text = "";
        }

        Renderer.SetSnapshot(snap);   // ScrollOffset=0 (القاع) يبقى ملتصقاً بالأسفل تلقائياً
        UpdateScrollBar();
        UpdateComposerVisibility();   // الشاشة البديلة تُخفي الصندوق، والعودة منها تُظهره
        UpdateComposerCwd(snap);      // باث المجلد في الصندوق يتبع الموجّه الحاليّ
    }

    /// <summary>
    /// يحدّث باث المجلد المعروض في الصندوق من سطر الموجّه الحاليّ (الكتلة المفتوحة). يستخرج المسار
    /// حسب نمط الصدفة (cmd/pwsh/bash) ويعرض آخر جزأين مختصرَين؛ يرتدّ لمجلد البدء عند الفشل.
    /// </summary>
    private void UpdateComposerCwd(ScreenSnapshot snap)
    {
        if (ComposerBar.Visibility != Visibility.Visible) return;

        // نمسح أسطر الموجّه الحاليّ (من بداية الكتلة المفتوحة للأسفل، وإلّا آخر ٦ أسطر) بحثاً عن مسار.
        // موجّه bash سطران (المسار على السطر الأعلى، و$ تحته)، فقراءة سطر واحد كانت تفشل ⇒ يرتدّ
        // للمسار الابتدائيّ ولا يتبع cd. المسح من الأسفل للأعلى يلتقط أحدث مسار مطبوع.
        string? cwd = null;
        if (!snap.AltScreen)
        {
            int from = snap.Lines.Count - 6;
            if (snap.Blocks is { Count: > 0 })
            {
                BlockSnapshot? open = null;
                foreach (var b in snap.Blocks) if (b.EndLine == long.MaxValue) open = b;
                if (open != null) from = (int)(open.StartLine - snap.BaseLine);
            }
            from = Math.Max(0, from);
            for (int i = snap.Lines.Count - 1; i >= from && cwd == null; i--)
                cwd = ExtractCwd(LinePlainText(snap.Lines[i]));
        }
        cwd ??= _entry.Path.Replace('\\', '/');
        ComposerCwd.Text = ShortenPath(cwd);
    }

    /// <summary>يستخرج مجلد العمل من نصّ الموجّه (cmd: ...&gt; · pwsh: PS ...&gt; · bash: توكِن مسار).</summary>
    private static string? ExtractCwd(string prompt)
    {
        prompt = prompt.TrimEnd();
        if (prompt.Length == 0) return null;

        // cmd / pwsh: ينتهي بـ '>' والمسار قبله (نزيل بادئة "PS " إن وُجدت).
        if (prompt.EndsWith(">"))
        {
            string p = prompt[..^1].Trim();
            if (p.StartsWith("PS ", StringComparison.Ordinal)) p = p[3..].Trim();
            if (p.Length is > 1 and < 260 && (p.Contains(":\\") || p.StartsWith("/") || p.StartsWith("~")))
                return p;
        }

        // bash/zsh: أوّل توكِن يشبه مساراً (/c/... أو ~/...).
        foreach (var tok in prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (tok.StartsWith("/") || tok.StartsWith("~/") || tok == "~")
                return tok;

        return null;
    }

    /// <summary>يطبّع المسار (شرطات مائلة) ويعرضه كاملاً — على سطره المستقلّ لا يحتاج اقتطاعاً.</summary>
    private static string ShortenPath(string path)
        => string.IsNullOrWhiteSpace(path) ? "" : path.Replace('\\', '/').TrimEnd('/');

    // ===== صندوق التأليف =====

    /// <summary>
    /// هل الصدفة عند موجّهها الفعليّ الجاهز (لا أمر تفاعليّ يعمل)؟ شرط <b>مزدوج وصارم</b> كي لا يتذبذب:
    /// (١) آخر سطر محتوى ينتهي برمز موجّه (<c>$ &gt; # % ❯</c>)، و(٢) وجود <b>مسار مجلد حقيقيّ</b> في
    /// آخر ٣ أسطر (يكشفه <see cref="ExtractCwd"/>). موجّه الصدفة يحمل الاثنين؛ أمّا واجهة claude/الوكيل
    /// الداخليّة (رمز <c>&gt;</c> بلا مسار) أو مخرجات تنتهي صدفةً برمز، فلا تحمل مساراً ⇒ يبقى الإنبت
    /// مخفيّاً والتحكّم عند الوكيل حتّى تعود الصدفة لموجّهها فعلاً. الشاشة البديلة (vim) تُخفيه فوراً.
    /// </summary>
    private bool IsAtShellPrompt(ScreenSnapshot? snap)
    {
        if (snap == null || snap.AltScreen) return false;

        // آخر سطر يحمل محتوى.
        int last = -1;
        for (int i = snap.Lines.Count - 1; i >= 0; i--)
            if (LinePlainText(snap.Lines[i]).TrimEnd().Length > 0) { last = i; break; }
        if (last < 0) return false;

        string lastLine = LinePlainText(snap.Lines[last]).TrimEnd();
        char c = lastLine[^1];
        bool hasTerminator = c is '$' or '>' or '#' or '%' or '❯';
        if (!hasTerminator) return false;

        // مسار مجلد حقيقيّ في آخر ٣ أسطر (يميّز موجّه الصدفة عن واجهة الوكيل ومن المخرجات العابرة).
        for (int i = last; i >= Math.Max(0, last - 2); i--)
            if (ExtractCwd(LinePlainText(snap.Lines[i])) != null) return true;

        return false;
    }

    /// <summary>
    /// يُظهر صندوق التأليف فقط حين تكون الصدفة <b>عند موجّه جاهز</b>؛ ويخفيه متى عمل أمر تفاعليّ
    /// (claude/الوكيل/less/vim/أيّ برنامج يقرأ الإدخال بنفسه) فتصير الشبكة هي الإدخال تلقائياً —
    /// فيتوافق مع تلك الأدوات. الإخفاء اليدويّ بـ Esc يبقى محترَماً.
    /// </summary>
    private DispatcherTimer? _composerHideTimer;

    private void UpdateComposerVisibility()
    {
        bool altScreen = _lastSnapshot?.AltScreen ?? false;
        bool wantShow = _composerEnabled && IsAtShellPrompt(_lastSnapshot) && !_composerSuppressReshow;

        if (wantShow)
        {
            CancelComposerHide();
            SetComposerShown(true);
        }
        else if (altScreen || !_composerEnabled || _composerSuppressReshow)
        {
            CancelComposerHide();      // إشارة قاطعة (شاشة بديلة/معطّل/Esc) ⇒ إخفاء فوريّ
            SetComposerShown(false);
        }
        else
        {
            // ليس عند موجّه لكن ليس شاشة بديلة (أمر يعمل): إخفاء مؤجّل ~٣٥٠مي يتفادى وميض
            // الأوامر السريعة (ls) ويُخفيه فعلاً للأدوات التفاعليّة الطويلة (claude/الوكيل).
            ScheduleComposerHide();
        }
    }

    private void ScheduleComposerHide()
    {
        if (ComposerBar.Visibility != Visibility.Visible || _composerHideTimer != null) return;
        _composerHideTimer = new DispatcherTimer(DispatcherPriority.Normal)
        { Interval = TimeSpan.FromMilliseconds(350) };
        _composerHideTimer.Tick += (_, _) =>
        {
            CancelComposerHide();
            if (!(_composerEnabled && IsAtShellPrompt(_lastSnapshot) && !_composerSuppressReshow))
                SetComposerShown(false);
        };
        _composerHideTimer.Start();
    }

    private void CancelComposerHide()
    {
        _composerHideTimer?.Stop();
        _composerHideTimer = null;
    }

    /// <summary>يُظهر/يخفي صندوق التأليف فعليّاً (مع التركيز ومؤشّر الشبكة).</summary>
    private void SetComposerShown(bool show)
    {
        if (show && ComposerBar.Visibility != Visibility.Visible)
        {
            ComposerBar.Visibility = Visibility.Visible;
            Renderer.SuppressCursor = true;   // الإدخال في الصندوق ⇒ لا مؤشّر في الشبكة
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ComposerBar.Visibility == Visibility.Visible
                    && !ComposerInput.IsKeyboardFocusWithin
                    && !SearchInput.IsKeyboardFocusWithin
                    && !EditorPanel.IsKeyboardFocusWithin)
                    ComposerInput.Focus();
            }), DispatcherPriority.Input);
        }
        else if (!show && ComposerBar.Visibility == Visibility.Visible)
        {
            ComposerBar.Visibility = Visibility.Collapsed;
            HideSuggestions();
            Renderer.SuppressCursor = false;
            // التحكّم ينتقل للتطبيق التفاعليّ (claude/vim/الوكيل): نُركّز الشبكة كي تصل كلّ ضغطة مفتاح
            // إليه مباشرةً (حرف-بحرف) بلا نقرة يدويّة — إلّا إن كان التركيز في البحث/المحرّر. مؤجَّل
            // كي يثبت بعد إخفاء الصندوق.
            if (!SearchInput.IsKeyboardFocusWithin && !EditorPanel.IsKeyboardFocusWithin)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ComposerBar.Visibility != Visibility.Visible) Renderer.Focus();
                }), DispatcherPriority.Input);
        }
        else
        {
            Renderer.SuppressCursor = show;
        }
    }

    // ===== محرّك الاقتراحات (الإكمال التلقائيّ داخل الصندوق) =====

    /// <summary>عنصر اقتراح في قائمة الصندوق: نصّ الأمر + أيقونة + وسم نوع (تاريخ/أمر/ملفّ).</summary>
    public sealed class ComposerSuggestion
    {
        public string Text { get; init; } = "";
        public string Icon { get; init; } = "";
        public string Kind { get; init; } = "";
    }

    /// <summary>أوامر شائعة تُقترَح حسب عائلة الصدفة (تكمِّل التاريخ حين يكون فارغاً/قصيراً).</summary>
    private static readonly string[] CommonUnix =
        { "ls", "cd ", "cat ", "grep ", "git status", "git add .", "git commit -m \"\"", "git push",
          "git pull", "git log --oneline", "npm run dev", "npm install", "npm run build", "code .",
          "mkdir ", "rm -rf ", "cp ", "mv ", "clear", "pwd", "chmod +x " };
    private static readonly string[] CommonPwsh =
        { "ls", "cd ", "Get-Content ", "Select-String ", "git status", "git add .", "git push",
          "git pull", "npm run dev", "npm install", "code .", "clear", "pwd", "Remove-Item -Recurse -Force " };

    /// <summary>يعيد بناء قائمة الاقتراحات والشبح من نصّ الصندوق الحاليّ.</summary>
    private void Composer_TextChanged(object sender, TextChangedEventArgs e)
    {
        _histIndex = -1;   // الكتابة تُنهي تنقّل التاريخ
        if (ComposerInput.IsKeyboardFocusWithin) { ShowComposerCaret(true); PositionComposerCaret(); }
        string text = ComposerInput.Text;

        // متعدّد الأسطر ⇒ لا اقتراحات (نصّ مركّب) — نُخفيها ونمسح الشبح.
        if (text.Contains('\n') || text.Length == 0)
        {
            HideSuggestions();
            SetGhost(null, "");
            return;
        }

        var matches = BuildSuggestions(text);
        if (matches.Count == 0)
        {
            HideSuggestions();
            SetGhost(null, "");
            return;
        }

        SuggestList.ItemsSource = matches;
        SuggestList.SelectedIndex = 0;
        SuggestPopup.IsOpen = true;

        SetGhost(matches[0].Text, text);   // الشبح = ذيل أعلى اقتراح (إن كان بادئةً للنصّ)
    }

    /// <summary>
    /// يضبط الشبح inline: يعرض <b>ذيل</b> الاقتراح (ما بعد المكتوب) بلون أفتح مكتوم مع حبّة tab، ويضعه
    /// أفقيّاً عند نهاية النصّ المكتوب بدقّة (GetRectFromCharacterIndex) فيتحاذى مع الكتابة تماماً.
    /// تمرير suggestion=null يُخفي الشبح.
    /// </summary>
    private void SetGhost(string? suggestion, string typed)
    {
        bool ok = suggestion != null && typed.Length > 0
               && suggestion.StartsWith(typed, StringComparison.OrdinalIgnoreCase)
               && suggestion.Length > typed.Length;
        if (!ok)
        {
            ComposerGhostPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ComposerGhost.Text = suggestion!.Substring(typed.Length);   // الذيل فقط
        _ghostSuggestion = suggestion;
        ComposerGhostPanel.Visibility = Visibility.Visible;

        // الموضع الأفقيّ = طرف آخر حرف مكتوب؛ يُؤجَّل لِما بعد إعادة التخطيط كي يكون الطرف محدَّثاً.
        Dispatcher.BeginInvoke(new Action(PositionGhost), DispatcherPriority.Loaded);
    }

    private string? _ghostSuggestion;

    /// <summary>يضع طبقة الشبح عند نهاية النصّ المكتوب رأسيّاً ووسطاً.</summary>
    private void PositionGhost()
    {
        if (ComposerGhostPanel.Visibility != Visibility.Visible) return;
        try
        {
            int idx = Math.Max(0, ComposerInput.Text.Length - 1);
            var rect = ComposerInput.GetRectFromCharacterIndex(ComposerInput.Text.Length, true);
            if (rect.IsEmpty) rect = ComposerInput.GetRectFromCharacterIndex(idx, true);
            if (rect.IsEmpty) return;
            Canvas.SetLeft(ComposerGhostPanel, rect.X);
            // توسيط رأسيّ مع سطر الكتابة.
            double h = ComposerGhostPanel.ActualHeight > 0 ? ComposerGhostPanel.ActualHeight : 18;
            Canvas.SetTop(ComposerGhostPanel, rect.Y + (rect.Height - h) / 2);
        }
        catch { /* التخطيط لم يجهز بعد — سيُعاد الضبط عند التغيير التالي */ }
    }

    /// <summary>
    /// يبني اقتراحات مرتّبة من: تاريخ الجلسة، ثمّ السجلّ العامّ، ثمّ ملفات المجلد، ثمّ الأوامر الشائعة.
    /// البادئة تسبق التطابق الجزئيّ، وبلا تكرار، بحدٍّ أقصى معقول.
    /// </summary>
    private List<ComposerSuggestion> BuildSuggestions(string text)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefix = new List<ComposerSuggestion>();
        var contains = new List<ComposerSuggestion>();

        void Consider(string cmd, string icon, string kind)
        {
            cmd = cmd.Trim();
            if (cmd.Length == 0 || cmd.Equals(text, StringComparison.OrdinalIgnoreCase)) return;
            if (!seen.Add(cmd)) return;
            if (cmd.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                prefix.Add(new ComposerSuggestion { Text = cmd, Icon = icon, Kind = kind });
            else if (cmd.Contains(text, StringComparison.OrdinalIgnoreCase))
                contains.Add(new ComposerSuggestion { Text = cmd, Icon = icon, Kind = kind });
        }

        // 1) تاريخ الجلسة (الأحدث أوّلاً).
        for (int i = _sessionCommands.Count - 1; i >= 0; i--) Consider(_sessionCommands[i], "🕘", "session");
        // 2) السجلّ العامّ.
        try { foreach (var c in _history.Recent(200)) Consider(c, "🕘", "history"); } catch { }
        // 3) ملفات/مجلدات المجلد الحاليّ (تكمِّل الكلمة الأخيرة — مسارات).
        foreach (var f in CwdCompletions(text)) Consider(f, "📄", "file");
        // 4) أوامر شائعة حسب الصدفة.
        foreach (var c in (IsPwsh ? CommonPwsh : CommonUnix)) Consider(c, "⌘", "command");

        var result = new List<ComposerSuggestion>(prefix.Count + contains.Count);
        result.AddRange(prefix);
        result.AddRange(contains);
        if (result.Count > 12) result.RemoveRange(12, result.Count - 12);
        return result;
    }

    /// <summary>هل الصدفة من عائلة PowerShell؟ (لاختيار قائمة الأوامر الشائعة).</summary>
    private bool IsPwsh => _entry.Shell.Contains("powershell", StringComparison.OrdinalIgnoreCase)
                        || _entry.Shell.Contains("pwsh", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// إكمال الكلمة الأخيرة من المسار: يقرأ محتوى مجلد العمل (باث المشروع) ويعيد أوامر مكتملة بأسماء
    /// الملفات/المجلدات المطابقة. best-effort — يتجاهل الأخطاء ولا يتتبّع cd (يستعمل مجلد البدء).
    /// </summary>
    private IEnumerable<string> CwdCompletions(string text)
    {
        string dir = _entry.Path;
        if (string.IsNullOrWhiteSpace(dir) || !System.IO.Directory.Exists(dir)) yield break;

        int sp = text.LastIndexOf(' ');
        string head = sp >= 0 ? text[..(sp + 1)] : "";
        string frag = sp >= 0 ? text[(sp + 1)..] : text;
        if (frag.Length == 0) yield break;

        string[] entries;
        try { entries = System.IO.Directory.GetFileSystemEntries(dir); } catch { yield break; }

        int n = 0;
        foreach (var full in entries)
        {
            string name = System.IO.Path.GetFileName(full);
            if (name.StartsWith(frag, StringComparison.OrdinalIgnoreCase))
            {
                bool isDir = System.IO.Directory.Exists(full);
                yield return head + name + (isDir ? "/" : "");
                if (++n >= 20) yield break;
            }
        }
    }

    private void HideSuggestions()
    {
        SuggestPopup.IsOpen = false;
        SuggestList.ItemsSource = null;
    }

    /// <summary>يقبل اقتراحاً: يستبدل نصّ الصندوق به ويضع المؤشّر في نهايته ويُخفي القائمة.</summary>
    private void AcceptSuggestion(string cmd)
    {
        ComposerInput.Text = cmd;
        ComposerInput.CaretIndex = cmd.Length;
        ClearGhost();
        HideSuggestions();
        ComposerInput.Focus();
    }

    private void SuggestList_Click(object sender, MouseButtonEventArgs e)
    {
        if (SuggestList.SelectedItem is ComposerSuggestion s) { AcceptSuggestion(s.Text); e.Handled = true; }
        else if (SuggestList.Items.Count > 0 && SuggestList.Items[0] is ComposerSuggestion top) AcceptSuggestion(top.Text);
    }

    /// <summary>
    /// مفاتيح صندوق التأليف: Enter يُرسل الأمر (متعدّد الأسطر عبر Bracketed Paste)، Shift+Enter سطر
    /// جديد، Esc يعيد التركيز للشبكة، Ctrl+C يُفرغه، والأسهم عند الحدّ تتنقّل تاريخ الجلسة.
    /// </summary>
    private void Composer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        bool suggesting = SuggestPopup.IsOpen && SuggestList.Items.Count > 0;

        switch (e.Key)
        {
            // Tab / السهم الأيمن عند النهاية: يقبل الشبح (الاقتراح الأعلى) — نمط Warp.
            case Key.Tab when !shift:
            case Key.Right when ComposerInput.CaretIndex == ComposerInput.Text.Length
                             && _ghostSuggestion != null:
                if (_ghostSuggestion != null) { AcceptSuggestion(_ghostSuggestion); e.Handled = true; }
                else if (suggesting && SuggestList.SelectedItem is ComposerSuggestion s1) { AcceptSuggestion(s1.Text); e.Handled = true; }
                break;

            // الأسهم تتنقّل قائمة الاقتراحات إن كانت ظاهرة، وإلّا تاريخ الجلسة.
            case Key.Down:
                if (suggesting)
                {
                    SuggestList.SelectedIndex = Math.Min(SuggestList.SelectedIndex + 1, SuggestList.Items.Count - 1);
                    SuggestList.ScrollIntoView(SuggestList.SelectedItem);
                    SyncGhostToSelection();
                    e.Handled = true;
                }
                else if (!ComposerIsMultiline() && NavigateComposerHistory(older: false)) e.Handled = true;
                break;
            case Key.Up:
                if (suggesting)
                {
                    SuggestList.SelectedIndex = Math.Max(SuggestList.SelectedIndex - 1, 0);
                    SuggestList.ScrollIntoView(SuggestList.SelectedItem);
                    SyncGhostToSelection();
                    e.Handled = true;
                }
                else if (!ComposerIsMultiline() && NavigateComposerHistory(older: true)) e.Handled = true;
                break;

            case Key.Enter when !shift:
                // اقتراح مُحدَّد بالأسهم (غير الأوّل) ⇒ Enter يقبله بدل الإرسال؛ وإلّا يُرسل.
                if (suggesting && SuggestList.SelectedIndex > 0
                    && SuggestList.SelectedItem is ComposerSuggestion s2)
                    AcceptSuggestion(s2.Text);
                else
                    SubmitComposer();
                e.Handled = true;
                break;

            case Key.Escape:
                if (suggesting) { HideSuggestions(); ClearGhost(); e.Handled = true; break; }
                _composerSuppressReshow = true;   // إخفاء يدويّ حتّى تركيز/Enter لاحق
                ComposerBar.Visibility = Visibility.Collapsed;
                Renderer.SuppressCursor = false;
                Renderer.Focus();
                e.Handled = true;
                break;

            case Key.C when ctrl:
                if (ComposerInput.SelectionLength == 0)
                {
                    ComposerInput.Clear();
                    _histIndex = -1;
                    e.Handled = true;
                }
                break;

            case Key.L when ctrl:
                ClearButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    /// <summary>يضبط الشبح ليطابق العنصر المحدَّد في القائمة (عند التنقّل بالأسهم).</summary>
    private void SyncGhostToSelection()
    {
        if (SuggestList.SelectedItem is ComposerSuggestion s) SetGhost(s.Text, ComposerInput.Text);
        else ClearGhost();
    }

    /// <summary>يُخفي طبقة الشبح ويمسح اقتراحه المخزَّن.</summary>
    private void ClearGhost()
    {
        ComposerGhostPanel.Visibility = Visibility.Collapsed;
        _ghostSuggestion = null;
    }

    /// <summary>تركيز الصندوق يلغي كتم إعادة الإظهار (المستخدم عاد إليه بإرادته بعد Esc) ويُظهر المؤشّر.</summary>
    private void Composer_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _composerSuppressReshow = false;
        ShowComposerCaret(true);
        PositionComposerCaret();
    }

    /// <summary>فقدان التركيز يُخفي المؤشّر المخصّص (لا نُبقيه واقفاً في صندوق غير نشط).</summary>
    private void Composer_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        => ShowComposerCaret(false);

    /// <summary>تحرّك المؤشّر/التحديد يعيد وضع المؤشّر المخصّص (وميضه يعود ظاهراً فوراً).</summary>
    private void Composer_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (ComposerInput.IsKeyboardFocusWithin) { ShowComposerCaret(true); PositionComposerCaret(); }
    }

    private void ShowComposerCaret(bool show)
    {
        ComposerCaret.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ComposerCaret.Opacity = 1.0;   // يعود ظاهراً فوراً عند كلّ حركة (لا ينتظر دورة وميض)
    }

    /// <summary>يضع المؤشّر المخصّص عند موضع الكتابة (CaretIndex) بدقّة عبر GetRectFromCharacterIndex.</summary>
    private void PositionComposerCaret()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                int ci = ComposerInput.CaretIndex;
                var rect = ComposerInput.GetRectFromCharacterIndex(ci, false);
                if (rect.IsEmpty && ci > 0) rect = ComposerInput.GetRectFromCharacterIndex(ci - 1, true);
                if (rect.IsEmpty) { Canvas.SetLeft(ComposerCaret, 0); Canvas.SetTop(ComposerCaret, 2); return; }
                ComposerCaret.Height = rect.Height > 4 ? rect.Height : 18;
                Canvas.SetLeft(ComposerCaret, rect.X);
                Canvas.SetTop(ComposerCaret, rect.Y);
            }
            catch { /* التخطيط لم يجهز — يُعاد عند الحركة التالية */ }
        }), DispatcherPriority.Loaded);
    }

    /// <summary>هل نصّ الصندوق متعدّد الأسطر فعلاً؟ (عندئذ الأسهم تحرّك المؤشّر لا التاريخ).</summary>
    private bool ComposerIsMultiline() => ComposerInput.Text.Contains('\n');

    /// <summary>يُرسل محتوى الصندوق للصدفة ثمّ يُفرغه ويسجّله في التاريخ. الفارغ يُرسل سطراً فارغاً.</summary>
    private void SubmitComposer()
    {
        string text = ComposerInput.Text;
        ComposerInput.Clear();
        ClearGhost();
        HideSuggestions();
        _histIndex = -1;

        string trimmed = text.Trim();
        if (trimmed.Length > 0) RecordHistory(trimmed);

        // بديل الكتلة الاستدلاليّ: بدء كتلة أمر جديدة (يُتجاهَل تحت OSC 133 الحقيقيّ).
        lock (_screenLock) _coreScreen?.BeginHeuristicCommand(trimmed);

        // متعدّد الأسطر ⇒ Bracketed Paste كي تتلقّاه الصدفة كنصّ واحد لا كأوامر منفصلة، ثمّ سطر تنفيذ.
        if (text.Contains('\n'))
        {
            SendPaste(text.Replace("\r\n", "\n").TrimEnd('\n'));
            Send(_newline);
        }
        else
        {
            Send(text + _newline);
        }

        ClearInputTracking();   // الصندوق هو مصدر الإدخال الآن — لا شبح داخل الشبكة
        Renderer.ScrollOffset = 0;   // القفز للقاع كي تُرى المخرجات
    }

    /// <summary>يملأ الصندوق من تاريخ الجلسة (سهم أعلى=أقدم). يعيد false إن لا تنقّل ممكن.</summary>
    private bool NavigateComposerHistory(bool older)
    {
        if (_sessionCommands.Count == 0) return false;

        if (_histIndex == -1)
        {
            if (!older) return false;              // Down بلا تنقّل نشط ⇒ لا شيء
            _histSavedLine = ComposerInput.Text;   // احفظ السطر الحيّ قبل الاستبدال
            _histIndex = _sessionCommands.Count;
        }

        int next = older ? _histIndex - 1 : _histIndex + 1;
        if (next < 0) return true;                 // تجاوزنا الأقدم ⇒ ابقَ (ابتلاع السهم)
        if (next >= _sessionCommands.Count)
        {
            _histIndex = -1;                       // تجاوزنا الأحدث ⇒ استعد السطر الحيّ
            ComposerInput.Text = _histSavedLine ?? "";
            ComposerInput.CaretIndex = ComposerInput.Text.Length;
            return true;
        }

        _histIndex = next;
        ComposerInput.Text = _sessionCommands[_histIndex];
        ComposerInput.CaretIndex = ComposerInput.Text.Length;
        return true;
    }

    /// <summary>يُزامن شريط التمرير مع هندسة العارض (الأعلى=التمرير للأعلى) دون إطلاق حلقة تغذية.</summary>
    private void UpdateScrollBar()
    {
        _syncingScroll = true;
        try
        {
            Scroll.Minimum = 0;
            Scroll.Maximum = Renderer.MaxScrollOffset;
            Scroll.ViewportSize = Renderer.VisibleRows;
            Scroll.Value = Renderer.MaxScrollOffset - Renderer.ScrollOffset;
        }
        finally { _syncingScroll = false; }
    }

    private void Scroll_Scroll(object sender, ScrollEventArgs e)
    {
        if (_syncingScroll || _lastSnapshot == null) return;
        int offset = (int)Math.Round(Scroll.Maximum - Scroll.Value);
        Renderer.ScrollOffset = Math.Clamp(offset, 0, Renderer.MaxScrollOffset);
        Renderer.SetSnapshot(_lastSnapshot);
        UpdateScrollBar();
    }

    /// <summary>يمسح النموذج ويعيد ضبط العارض ومؤشّرات البحث/التحديد.</summary>
    private void ClearDocument()
    {
        lock (_screenLock) _coreScreen?.Clear();
        _lastSnapshot = null;
        // المسح/إعادة التشغيل يصفّر أسطر المحرّك المطلقة ⇒ مفاتيح StartLine المتتبَّعة تصبح صالحة
        // لكتلٍ جديدة مختلفة تماماً؛ نُسقطها كي لا تُنسب بدايةٌ قديمة لأمرٍ جديد (T-211).
        _runningBlocks.Clear();
        _matches.Clear();
        _matchIndex = -1;
        Renderer.ClearSearchMatches();
        Renderer.ClearSelection();
        Renderer.ScrollOffset = 0;
        ClearInputTracking();   // مسح الشاشة يبطل السطر المتتبَّع والشبح (T-205)
        MarkDirty();   // يعيد الرسم من اللقطة النظيفة
        UpdateScrollBar();
    }

    private void OnExited(int exitCode) => Dispatcher.BeginInvoke(() =>
    {
        _statusTimer.Stop();
        _endTime = DateTime.Now;
        var brush = (Brush)FindResource(exitCode == 0 ? "Brush.Success" : "Brush.Danger");
        SetStatus($"انتهى · رمز {exitCode} · ⏱ {Elapsed()}", brush);
    });

    private void ResizeSession()
    {
        if (_coreSession == null) return;
        var (cols, rows) = Measure();
        lock (_screenLock) _coreScreen?.Resize(cols, rows);
        MarkDirty();   // إعادة التدفّق تتطلّب إعادة رسم الشبكة
        _coreSession.Resize(cols, rows);
    }

    /// <summary>يحدّث الحالة أثناء التشغيل (PID + المدّة المنقضية) كل ثانية.</summary>
    private void RefreshRunningStatus()
        => SetStatus($"يعمل · PID {_pid} · ⏱ {Elapsed()}", (Brush)FindResource("Brush.TextMuted"));

    /// <summary>المدّة المنقضية (mm:ss أو h:mm:ss) حتى الانتهاء أو اللحظة الحاليّة.</summary>
    private string Elapsed()
    {
        var span = (_endTime ?? DateTime.Now) - _startTime;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    private void SetStatus(string status, Brush? brush = null)
    {
        StatusText.Text = status;
        StatusText.Foreground = brush ?? (Brush)FindResource("Brush.TextMuted");
    }

    // ===== شريط الأدوات =====

    // إيقاف التحديث الدوريّ أثناء فتح القائمة المنسدلة: المحرّك قد يعيد الرسم باستمرار
    // (وميض مؤشّر PowerShell مثلاً) فيُشبِع خيط الواجهة ويجعل الكومبو يبدو غير قابل للتغيير.
    private void ShellCombo_DropDownOpened(object sender, EventArgs e) => _refresh.Stop();
    private void ShellCombo_DropDownClosed(object sender, EventArgs e) { if (_started) _refresh.Start(); }

    private void ShellCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (ShellCombo.SelectedItem is ShellDef shell && shell.Key != _entry.Shell)
        {
            _entry.Shell = shell.Key;   // تغيير الافتراضي حيّاً
            _persist();                 // يُحفظ
            if (_started) StartSession();
            Renderer.Focus();
        }
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        StartSession();
        Renderer.Focus();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _statusTimer.Stop();
        _endTime = DateTime.Now;
        _coreSession?.Dispose();
        _coreSession = null;
        SetStatus($"متوقف · ⏱ {Elapsed()}");
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ClearDocument();
        Renderer.Focus();
    }

    private void ExpandToggle_Click(object sender, RoutedEventArgs e)
    {
        bool expanded = _toggleSidebar();
        ExpandToggle.IsChecked = expanded;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this);

    /// <summary>
    /// زرّ الفصل: نقرة عاديّة → يطلب فصل هذا التيرمنال إلى نافذة مستقلّة (النافذة الرئيسة تتولّاه)؛
    /// Shift+نقرة → يفتح تيرمنالاً <b>خارجيّاً</b> (نافذة نظام حقيقيّة) بنفس صدفة ومسار هذا الجزء.
    /// </summary>
    private void DetachButton_Click(object sender, RoutedEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) OpenExternalTerminal();
        else DetachRequested?.Invoke(this);
    }

    /// <summary>يطلق نافذة تيرمنال نظام خارجيّة بصدفة هذا الجزء ومجلّد عمله (مسار الأمر أو مجلّد البروفايل).</summary>
    private void OpenExternalTerminal()
    {
        var profile = ShellCatalog.GetProfile(_entry.Shell);
        string exe = !string.IsNullOrWhiteSpace(profile?.ExePath) ? profile!.ExePath! : "cmd.exe";
        string? args = profile?.Arguments;

        string workDir = !string.IsNullOrWhiteSpace(profile?.WorkingDirectory) ? profile!.WorkingDirectory!
            : (!string.IsNullOrWhiteSpace(_entry.Path) ? _entry.Path! : Environment.CurrentDirectory);
        if (!System.IO.Directory.Exists(workDir)) workDir = Environment.CurrentDirectory;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,   // نافذة كونسول نظام مستقلّة
                WorkingDirectory = workDir,
            };
            if (!string.IsNullOrWhiteSpace(args)) psi.Arguments = args;
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            TerminalLauncher.Views.AppDialog.Alert(Window.GetWindow(this), "تنبيه", $"تعذّر فتح تيرمنال خارجيّ: {ex.Message}");
        }
    }

    // ===== سجلّ الأوامر (T-106): التقاط + استدعاء =====

    /// <summary>يسجّل أمراً منفَّذاً في المخزن (يتجاهل الفراغ)؛ يُغلَّف كي لا يكسر خزنٌ عابرٌ التيرمنال.</summary>
    private void RecordHistory(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        try { _history.Add(command, _entry.Shell, _entry.Path); } catch { }

        // تاريخ الجلسة (تنقّل الأسهم + إعادة تنفيذ آخر أمر): نتجاهل التكرار المتتالي.
        string cmd = command.Trim();
        if (cmd.Length == 0) return;
        if (_sessionCommands.Count == 0 || _sessionCommands[^1] != cmd)
        {
            _sessionCommands.Add(cmd);
            try { _sessionHistory.Append(SessionId, cmd); } catch { }
        }
        _histIndex = -1;   // أمرٌ جديد نُفِّذ ⇒ يبدأ التنقّل التالي من النهاية
    }

    /// <summary>
    /// يسجّل نصّ أمر آخر كتلة مكتملة (State != Running) من اللقطة إن تغيّر عن آخر ما سُجِّل،
    /// كي لا تُكرِّر حلقة التحديث (40ms) نفس الأمر. يغطّي أصداف تكامل OSC 133.
    /// </summary>
    private void CaptureCompletedBlockCommand(ScreenSnapshot snap)
    {
        if (snap.Blocks.Count == 0) return;
        for (int i = snap.Blocks.Count - 1; i >= 0; i--)
        {
            var b = snap.Blocks[i];
            if (b.State == BlockState.Running) continue;
            string cmd = b.CommandText;
            if (string.IsNullOrWhiteSpace(cmd)) return;   // أحدث كتلة مكتملة بلا نصّ ⇒ لا شيء لنسجّله
            if (cmd == _lastRecordedBlockCommand) return; // مسجَّل سابقاً ⇒ تجاهُل التكرار
            _lastRecordedBlockCommand = cmd;
            RecordHistory(cmd);
            return;
        }
    }

    // ===== إشعار انتهاء الأوامر الطويلة (T-211) =====

    /// <summary>
    /// يتتبّع الكتل الجارية ويكتشف انتقالها إلى مكتملة، فيُطلق إشعاراً للأوامر الطويلة.
    /// المحرّك لا يختم الكتل زمنيّاً، فنختم أوّل رؤية للكتلة وهي تعمل ونحسب الفارق عند اكتمالها.
    /// إزالة المدخلة عند الاكتمال هي آليّة منع التكرار: حلقة التحديث (40ms) ترى الكتلة مكتملةً
    /// آلاف المرّات بعدها، لكن <see cref="Dictionary{TKey,TValue}.Remove(TKey, out TValue)"/>
    /// ينجح مرّةً واحدة فقط. كتلةٌ لم نرها تعمل قطّ (كانت مكتملةً عند أوّل لقطة) تُتجاهَل: لا مدّة لها.
    /// </summary>
    private void TrackLongCommandBlocks(ScreenSnapshot snap)
    {
        if (snap.Blocks.Count == 0) return;
        foreach (var b in snap.Blocks)
        {
            if (b.State == BlockState.Running)
            {
                if (_runningBlocks.TryGetValue(b.StartLine, out var tracked))
                {
                    // نراكم «رأت شاشة بديلة» طوال عمل الكتلة: التطبيق الكامل (vim) يستعيد الشاشة
                    // الرئيسة قبل خروجه، فلقطة لحظة الاكتمال وحدها لا تكفي للتعرّف عليه.
                    if (snap.AltScreen && !tracked.SawAlt)
                        _runningBlocks[b.StartLine] = (tracked.StartedAt, true);
                }
                else _runningBlocks[b.StartLine] = (System.Diagnostics.Stopwatch.GetTimestamp(), snap.AltScreen);
            }
            else if (_runningBlocks.Remove(b.StartLine, out var done))
            {
                NotifyCommandFinished(b, System.Diagnostics.Stopwatch.GetElapsedTime(done.StartedAt),
                                      done.SawAlt || snap.AltScreen);
            }
        }
    }

    /// <summary>
    /// يُظهر إشعار انتهاء أمرٍ طويل إن استوفى الشروط: مدّة ≥ <see cref="LongCommandThreshold"/>،
    /// ولم تكن الكتلة تطبيق شاشة بديلة (vim/htop ليس «أمراً» ننتظر انتهاءه)، ونصّ الأمر غير فارغ،
    /// والنافذة المستضيفة غير نشطة. النجاح/الفشل من <see cref="BlockSnapshot.State"/> — وهو مشتقٌّ
    /// من رمز الخروج في المحرّك (رمز مفقود أو 0 ⇒ نجاح)، فيغطّي الكتل بلا رمز دون تفريعٍ إضافيّ.
    /// </summary>
    private void NotifyCommandFinished(BlockSnapshot b, TimeSpan elapsed, bool sawAltScreen)
    {
        if (elapsed < LongCommandThreshold) return;   // أمر قصير ⇒ لا إزعاج
        if (sawAltScreen) return;                     // تطبيق كامل الشاشة ⇒ ليس أمراً
        if (IsHostWindowActive()) return;             // المستخدم ينظر إلى النافذة ⇒ صمت

        string cmd = ShortCommandText(b);
        if (cmd.Length == 0) return;                  // كتلة بلا نصّ أمر ⇒ لا شيء يُعرَض

        bool failed = b.State == BlockState.Failed;
        string title = Services.Loc.T(failed ? "notify.cmdFailed" : "notify.cmdDone");
        string message = $"{cmd} · {FormatDuration(elapsed)}";
        if (failed) Services.NotificationService.Error(title, message);
        else Services.NotificationService.Success(title, message);
    }

    /// <summary>
    /// النافذة المستضيفة لهذا الجزء نشطة؟ نسأل <see cref="Window.GetWindow"/> لا النافذة الرئيسة،
    /// لأنّ العرض قد يكون مفصولاً في <see cref="TerminalHostWindow"/>: المعيار هو النافذة التي
    /// يراها المستخدم فعلاً وفيها هذا التيرمنال. null (خارج شجرة مرئيّة) ⇒ غير نشطة: لا أحد يراه.
    /// </summary>
    private bool IsHostWindowActive() => Window.GetWindow(this)?.IsActive == true;

    /// <summary>
    /// نصّ أمر الكتلة في سطرٍ واحد مقصوصاً لـ<see cref="NotifyCommandMaxLength"/> محرفاً.
    /// كتل OSC 133 لا تحمل نصّ الأمر (المحرّك يملؤه للكتل الاستدلاليّة فقط)، فـ<see cref="BlockCommandText"/>
    /// يستخرجه من أسطر المحثّ؛ نأخذ آخر سطر غير فارغ كي يظهر الأمر لا زخرفةُ محثٍّ متعدّد الأسطر.
    /// </summary>
    private string ShortCommandText(BlockSnapshot b)
    {
        string raw = BlockCommandText(b);
        if (string.IsNullOrWhiteSpace(raw)) return "";

        string cmd = "";
        var lines = raw.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            cmd = lines[i].Trim();
            if (cmd.Length > 0) break;
        }
        if (cmd.Length == 0) return "";
        return cmd.Length <= NotifyCommandMaxLength
            ? cmd
            : cmd[..(NotifyCommandMaxLength - 1)].TrimEnd() + "…";
    }

    /// <summary>
    /// مدّة مقروءة بوحداتٍ محايدة لغويّاً وأرقام لاتينيّة: «45s» / «2m 14s» / «1h 5m».
    /// (تنسيق الأعداد الصحيحة في .NET لاتينيّ دائماً، كما في <see cref="Elapsed"/>.)
    /// </summary>
    private static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m {span.Seconds}s";
        return $"{span.Seconds}s";
    }

    // أحدث الأوامر المخزّنة (DISTINCT) المحمّلة عند فتح المنسدلة — يُصفّى منها البحث محلّياً.
    private List<string> _historyRecent = new();

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshHistoryList();
        HistorySearch.Text = "";
        HistoryPopup.IsOpen = true;
        // التركيز على مربّع البحث بعد فتح المنسدلة (مؤجَّل حتى يُنشأ العنصر بصريّاً).
        Dispatcher.BeginInvoke(new Action(() => HistorySearch.Focus()), DispatcherPriority.Input);
    }

    /// <summary>يحمّل أحدث الأوامر (DISTINCT) ويعرضها؛ يُصفَّى منها البحث لاحقاً.</summary>
    private void RefreshHistoryList()
    {
        try { _historyRecent = new List<string>(_history.Recent(300)); }
        catch { _historyRecent = new List<string>(); }
        FilterHistory("");
    }

    /// <summary>يصفّي السجلّ بمطابقة جزئيّة غير حسّاسة للحالة ويحدّد أوّل نتيجة.</summary>
    private void FilterHistory(string term)
    {
        IEnumerable<string> items = _historyRecent;
        if (!string.IsNullOrWhiteSpace(term))
            items = _historyRecent.Where(c => c.Contains(term, StringComparison.OrdinalIgnoreCase));
        var list = items.ToList();
        HistoryList.ItemsSource = list;
        if (list.Count > 0) HistoryList.SelectedIndex = 0;
    }

    private void HistorySearch_TextChanged(object sender, TextChangedEventArgs e)
        => FilterHistory(HistorySearch.Text);

    /// <summary>الأسهم أعلى/أسفل تتنقّل القائمة، Enter يلصق وينفّذ، Esc يغلق — مع بقاء التركيز في البحث.</summary>
    private void HistorySearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int count = HistoryList.Items.Count;
        switch (e.Key)
        {
            case Key.Down when count > 0:
                HistoryList.SelectedIndex = Math.Min(HistoryList.SelectedIndex + 1, count - 1);
                HistoryList.ScrollIntoView(HistoryList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                HistoryList.SelectedIndex = Math.Max(HistoryList.SelectedIndex - 1, 0);
                HistoryList.ScrollIntoView(HistoryList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                RunSelectedHistory();
                e.Handled = true;
                break;
            case Key.Escape:
                HistoryPopup.IsOpen = false;
                Renderer.Focus();
                e.Handled = true;
                break;
        }
    }

    private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => RunSelectedHistory();

    private void HistoryList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { RunSelectedHistory(); e.Handled = true; }
        else if (e.Key == Key.Escape) { HistoryPopup.IsOpen = false; Renderer.Focus(); e.Handled = true; }
    }

    /// <summary>يرسل الأمر المختار إلى الجلسة الحيّة (مع فاصل السطر) ويغلق المنسدلة.</summary>
    private void RunSelectedHistory()
    {
        if (HistoryList.SelectedItem is string cmd && !string.IsNullOrWhiteSpace(cmd))
            Send(cmd + _newline);
        ClearInputTracking();   // أمر مُرسَل مباشرةً يُبطِل السطر المتتبَّع والشبح (T-205)
        HistoryPopup.IsOpen = false;
        Renderer.Focus();
    }

    // ===== محاكي الكونسول: كتابة مباشرة =====

    /// <summary>هل صندوق التأليف نشط الآن (مُفعَّل وظاهر)؟ عندئذ هو مالك إدخال الأوامر لا الشبكة.</summary>
    private bool ComposerActive => _composerEnabled && ComposerBar.Visibility == Visibility.Visible;

    private void Renderer_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // الصندوق نشط ⇒ يملك إدخال الأوامر: نوجّه الكتابة إليه (نقر الشبكة ثمّ الكتابة يقفز للصندوق).
        if (ComposerActive)
        {
            ComposerInput.Focus();
            int caret = ComposerInput.CaretIndex;
            ComposerInput.Text = ComposerInput.Text.Insert(caret, e.Text);
            ComposerInput.CaretIndex = caret + e.Text.Length;
            e.Handled = true;
            return;
        }

        Send(e.Text);
        // تتبّع الإدخال المطبوع (T-205): نُلحق الأحرف القابلة للطباعة ثمّ نحدّث الشبح.
        AppendTypedText(e.Text);
        _histIndex = -1;   // الكتابة الفعليّة تُنهي تنقّل التاريخ
        UpdateGhost();
        e.Handled = true;
    }

    /// <summary>يُلحق نصّاً مطبوعاً بسطر الإدخال المتتبَّع (يتخطّى أحرف التحكّم كي لا يفسد التتبّع).</summary>
    private void AppendTypedText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (char c in text)
            if (!char.IsControl(c)) _inputLine.Append(c);
    }

    /// <summary>يمسح سطر الإدخال المتتبَّع والشبح (يُستدعى عند أيّ غموض في الحالة).</summary>
    private void ClearInputTracking()
    {
        _inputLine.Clear();
        Renderer.GhostText = null;
        _histIndex = -1;   // إنهاء تنقّل التاريخ
    }

    /// <summary>
    /// ينقل سطر الإدخال عبر تاريخ هذه الجلسة: <paramref name="older"/> نحو الأقدم، وإلّا نحو الأحدث.
    /// يستبدل السطر الحاليّ بالأمر المستدعى (أو يعيد السطر الحيّ عند تجاوز الأحدث). يعيد <c>false</c>
    /// إن لا تاريخ أو Down بلا تنقّل نشط (فيُمرَّر السهم للصدفة كسلوكها الأصليّ).
    /// </summary>
    private bool NavigateSessionHistory(bool older)
    {
        int count = _sessionCommands.Count;
        if (count == 0) return false;

        if (_histIndex < 0)   // بدء التنقّل
        {
            if (!older) return false;              // Down بلا تنقّل نشط ⇒ مرّر للصدفة
            _histSavedLine = _inputLine.ToString();
            _histIndex = count;                    // عند السطر الحيّ (النهاية)
        }

        if (older)
        {
            if (_histIndex == 0) { ReplaceInputLine(_sessionCommands[0]); return true; }   // عند الأقدم: ثبات
            _histIndex--;
            ReplaceInputLine(_sessionCommands[_histIndex]);
        }
        else
        {
            if (_histIndex >= count) return true;   // فوق الأحدث بالفعل
            _histIndex++;
            if (_histIndex >= count) { ReplaceInputLine(_histSavedLine); _histIndex = -1; }  // عودة للسطر الحيّ
            else ReplaceInputLine(_sessionCommands[_histIndex]);
        }
        return true;
    }

    /// <summary>يستبدل سطر الإدخال المرئيّ بنصّ جديد: يمسح المتتبَّع (backspaces) ثم يرسل النصّ، ويزامن التتبّع.</summary>
    private void ReplaceInputLine(string text)
    {
        if (_inputLine.Length > 0) Send(new string('\x7f', _inputLine.Length));   // مسح السطر الحاليّ (DEL لكلّ حرف)
        if (text.Length > 0) Send(text);
        _inputLine.Clear();
        _inputLine.Append(text);
        Renderer.GhostText = null;
    }

    /// <summary>
    /// يحدّث نصّ الشبح من سجلّ الأوامر: يُخفى على الشاشة البديلة أو حين خلوّ السطر؛ وإلّا يعرض
    /// بقيّة أحدث أمرٍ يبدأ بالسطر المتتبَّع. يُغلَّف وصول السجلّ كي لا يكسر خزنٌ عابرٌ التيرمنال.
    /// </summary>
    /// <summary>
    /// هل يملك التطبيقُ المُشغَّل تحريرَ سطر الإدخال بنفسه؟
    ///
    /// ميزاتنا المساعِدة (الشبح + تتبّع السطر + خطف Tab/الأسهم) تفترض أنّ <b>الصدفة</b> تملك السطر.
    /// مع تطبيق يحرّر سطره بنفسه <b>على الشاشة الأساس</b> (كلود كود وInk وREPL وpsql) تنقلب ضارّة:
    /// الشبح طبقةٌ نرسمها نحن لا وجود لها في ذاكرة الشاشة، فلا يستطيع التطبيق محوَها مهما مسح —
    /// وهذا سببُ «حرف يبقى عالقاً بعد مسح كلّ شيء».
    ///
    /// الإشارة القياسيّة أنّ التطبيق يملك سطره هي <b>اللصق المُقوَّس (DECSET 2004)</b>: يفعّله كلّ
    /// من يقرأ الإدخال بنفسه. نضمّ إليها الشاشة البديلة. (PSReadLine يفعّله أيضاً — وهذا مطلوب:
    /// له اقتراحه المدمج، فلا نرسم اقتراحاً ثانياً فوقه.)
    /// </summary>
    private bool AppOwnsInputLine()
    {
        if (_lastSnapshot?.AltScreen ?? false) return true;
        lock (_screenLock) return _coreScreen?.BracketedPaste ?? false;
    }

    private void UpdateGhost()
    {
        if (AppOwnsInputLine() || _inputLine.Length == 0)
        {
            Renderer.GhostText = null;
            return;
        }
        try
        {
            string prefix = _inputLine.ToString();
            string? s = _history.Suggest(prefix);
            Renderer.GhostText = (s == null) ? null : s.Substring(prefix.Length);
        }
        catch { Renderer.GhostText = null; }
    }

    /// <summary>
    /// يقبل الشبح المعروض (Tab أو السهم الأيمن): يرسل بقيّته للصدفة، يُلحقها بالسطر المتتبَّع،
    /// ويمسح الشبح. يعيد <c>true</c> إن كان هناك شبحٌ قُبِل (فيُعلَّم المفتاح مُعالَجاً).
    /// </summary>
    private bool TryAcceptGhost()
    {
        string? ghost = Renderer.GhostText;
        if (string.IsNullOrEmpty(ghost)) return false;
        Send(ghost);
        _inputLine.Append(ghost);
        Renderer.GhostText = null;
        return true;
    }

    private void Renderer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ResetCursorBlink();   // يبقى المؤشّر صلباً أثناء الكتابة (يعيد ضبط طور الوميض)
        var mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        bool alt = (mods & ModifierKeys.Alt) != 0;
        // Alt يُبدِّل e.Key إلى Key.System ويضع المفتاح الحقيقي في SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 1) اختصارات التطبيق المحجوزة (T-006.5) — تُفحَص أوّلاً (Alt غير مسموح فيها).
        if (!alt && AppShortcuts.TryGetValue((key, mods & ~ModifierKeys.Windows), out var action))
        {
            DispatchAction(action);
            e.Handled = true;
            return;
        }

        // 1.1) الصندوق نشط والشبكة تحمل التركيز (بعد نقرة تحديد مثلاً): مفاتيح تحرير/تنفيذ سطر الأمر
        // تخصّ الصندوق لا الشبكة — ننقل التركيز إليه ونعيد توجيه المفتاح كي لا يذهب للـPTY.
        if (ComposerActive && !ctrl && !alt
            && key is Key.Enter or Key.Back or Key.Left or Key.Right or Key.Up or Key.Down
                    or Key.Home or Key.End or Key.Delete)
        {
            ComposerInput.Focus();
            if (key == Key.Enter) { SubmitComposer(); e.Handled = true; }
            // مفاتيح التحرير الأخرى: التركيز انتقل للصندوق، فيلتقطها هو في الضغطة التالية.
            return;
        }

        // كان الصندوق مخفيّاً يدويّاً بـ Esc والمستخدم يكتب في الشبكة: أوّل Enter يُنتِج موجّهاً جديداً
        // ⇒ نلغي الكتم فيعود الصندوق مع اللقطة التالية (وإلّا بقي مخفيّاً حتّى إعادة التشغيل).
        if (_composerSuppressReshow && _composerEnabled && key == Key.Enter)
            _composerSuppressReshow = false;

        // التطبيق يحرّر سطره بنفسه ⇒ لا نخطف مفاتيحه ولا نتتبّع سطره ولا نرسم شبحاً فوقه.
        // (كان Tab/الأيمن يُسرقان من كلود كود، والأسهم تُسرق منه، وReplaceInputLine يحقن
        //  DEL×N + نصَّ التاريخ في محرّره فيفكّ تزامن نموذجه الداخليّ عن الشاشة.)
        bool appOwnsLine = AppOwnsInputLine();
        if (appOwnsLine && (_inputLine.Length > 0 || Renderer.GhostText != null))
            ClearInputTracking();

        // 1.5) الإكمال الشبحيّ (T-205): قبول/تتبّع/تنظيف قبل الترجمة إلى VT.
        // قبول الشبح بـ Tab أو السهم الأيمن (بلا معدِّلات) — يُعلَّم مُعالَجاً فلا يُرسَل للصدفة كذلك.
        if (!appOwnsLine && !ctrl && !alt && !shift && (key == Key.Tab || key == Key.Right) && TryAcceptGhost())
        {
            e.Handled = true;
            return;
        }

        // 1.6) تنقّل تاريخ الجلسة بالأسهم أعلى/أسفل — حين تملك الصدفةُ السطر فقط (تطبيقات مثل vim
        // وكلود كود تتلقّى الأسهم كالمعتاد). إن لم يكن هناك تاريخ نمرّر السهم للصدفة (سلوكها الأصليّ).
        if (!appOwnsLine && !ctrl && !alt && !shift && (key == Key.Up || key == Key.Down)
            && NavigateSessionHistory(older: key == Key.Up))
        {
            e.Handled = true;
            return;
        }
        // Backspace: أزِل آخر حرف من السطر المتتبَّع وحدّث الشبح (يُرسَل للصدفة كالمعتاد أدناه).
        if (!ctrl && !alt && key == Key.Back)
        {
            if (_inputLine.Length > 0) _inputLine.Length--;
            _histIndex = -1;   // تحرير السطر يُنهي تنقّل التاريخ
            UpdateGhost();
        }
        // Enter: «التعلّم» — سجّل السطر المتتبَّع (إن كان غير فارغ) قبل مسحه، ثمّ نظّف الشبح.
        else if (key == Key.Enter)
        {
            string typed = _inputLine.ToString().Trim();
            if (typed.Length > 0) RecordHistory(typed);
            ClearInputTracking();
        }
        // مفاتيح تجعل التتبّع غير موثوق ⇒ نظّف السطر والشبح (ثمّ عالِج المفتاح كالمعتاد أدناه).
        else if (ctrl || key is Key.Left or Key.Home or Key.End or Key.Escape or Key.Delete
                 or Key.PageUp or Key.PageDown)
        {
            ClearInputTracking();
        }

        // Ctrl+C: نسخ إن كان هناك تحديد، وإلا مقاطعة (0x03). نستثني Ctrl+Alt (AltGr) لتمرير الكتابة.
        if (ctrl && !alt && key == Key.C)
        {
            if (Renderer.HasSelection) { CopyToClipboard(Renderer.GetSelectedText()); }
            else Send("\x03");
            e.Handled = true; return;
        }
        // Ctrl+V: لصق عبر مسار الحماية (نافذة تأكيد للنصّ الخطر/متعدّد الأسطر + Bracketed Paste).
        if (ctrl && !alt && key == Key.V)
        {
            PasteFromClipboard();
            e.Handled = true; return;
        }
        if (ctrl && !alt && key >= Key.A && key <= Key.Z)
        {
            Send(((char)(key - Key.A + 1)).ToString()); // Ctrl+A..Z → 0x01..0x1A
            e.Handled = true; return;
        }

        // Ctrl+Backspace → BS (0x08) = «احذف كلمة» بالاصطلاح الشائع. يُفحَص قبل كتلة Ctrl+A..Z
        // أدناه لأنّ Key.Back ليس ضمن مداها، وقبل MapSpecialKey كي لا يُترجَم إلى DEL.
        if (ctrl && !alt && key == Key.Back)
        {
            Send("\b");
            e.Handled = true; return;
        }

        // Alt+حرف → ميتا (ESC + الحرف) — يستعمله vim/emacs وقوائم الصدفة.
        if (alt && !ctrl && key >= Key.A && key <= Key.Z)
        {
            Send("\x1b" + (char)('a' + (key - Key.A)));
            e.Handled = true; return;
        }

        // مفاتيح خاصّة → تسلسل VT (واعية بـ DECCKM: SS3 بدل CSI للأسهم/Home/End).
        bool appCursor;
        lock (_screenLock) appCursor = _coreScreen?.ApplicationCursorKeys ?? false;
        string? seq = MapSpecialKey(key, appCursor);

        if (seq != null)
        {
            // بديل استدلاليّ: إرسال أمر بـ Enter يبدأ كتلة جديدة (يُتجاهَل تحت OSC 133 أو الشاشة البديلة).
            if (key == Key.Enter)
                lock (_screenLock) _coreScreen?.BeginHeuristicCommand("");
            Send(seq);
            e.Handled = true;
        }
    }

    /// <summary>ينفّذ اختصار تطبيق محجوزاً (من جدول <see cref="AppShortcuts"/>).</summary>
    private void DispatchAction(TermAction action)
    {
        switch (action)
        {
            case TermAction.Search: OpenSearch(); break;
            case TermAction.BlockPrev: JumpBlock(-1); break;
            case TermAction.BlockNext: JumpBlock(+1); break;
            // Ctrl+Shift+C (T-104.3): ينسخ التحديد إن وُجد، وإلّا يرجع لنسخ الكتلة عند أعلى الصفّ الظاهر.
            case TermAction.CopyBlock:
                if (Renderer.HasSelection) CopyToClipboard(Renderer.GetSelectedText());
                else CopyCurrentBlock();
                break;
            case TermAction.ZoomIn: Zoom(+1); break;
            case TermAction.ZoomOut: Zoom(-1); break;
            case TermAction.ZoomReset: ResetZoom(); break;
            case TermAction.Copy: if (Renderer.HasSelection) CopyToClipboard(Renderer.GetSelectedText()); break;
            case TermAction.Paste: PasteFromClipboard(); break;
            case TermAction.Close: CloseRequested?.Invoke(this); break;
        }
    }

    /// <summary>يترجم مفتاحاً خاصّاً إلى تسلسل VT؛ الأسهم/Home/End تتبع DECCKM (SS3 عند نمط التطبيق).</summary>
    private string? MapSpecialKey(Key key, bool appCursor)
    {
        string Cursor(char c) => appCursor ? $"\x1bO{c}" : $"\x1b[{c}";
        return key switch
        {
            Key.Enter => _newline,
            // Backspace المجرّد يرسل DEL (0x7F) لا BS (0x08) — هذا ما ترسله كلّ الطرفيّات الحديثة
            // (xterm/Windows Terminal/iTerm) وما تتوقّعه محرّرات الأسطر وتطبيقات TUI.
            // ‏0x08 محجوز اصطلاحاً لـ Ctrl+Backspace = «احذف كلمة» (يُعالَج في Renderer_PreviewKeyDown)،
            // فإرساله للضغطة المجرّدة كان يجعل التطبيق يحذف كلمةً كاملة في كلّ ضغطة.
            Key.Back => "\x7f",
            Key.Tab => "\t",
            Key.Escape => "\x1b",
            Key.Up => Cursor('A'),
            Key.Down => Cursor('B'),
            Key.Right => Cursor('C'),
            Key.Left => Cursor('D'),
            Key.Home => Cursor('H'),
            Key.End => Cursor('F'),
            Key.Insert => "\x1b[2~",
            Key.Delete => "\x1b[3~",
            Key.PageUp => "\x1b[5~",
            Key.PageDown => "\x1b[6~",
            Key.F1 => "\x1bOP",
            Key.F2 => "\x1bOQ",
            Key.F3 => "\x1bOR",
            Key.F4 => "\x1bOS",
            Key.F5 => "\x1b[15~",
            Key.F6 => "\x1b[17~",
            Key.F7 => "\x1b[18~",
            Key.F8 => "\x1b[19~",
            Key.F9 => "\x1b[20~",
            Key.F10 => "\x1b[21~",
            Key.F11 => "\x1b[23~",
            Key.F12 => "\x1b[24~",
            _ => null,
        };
    }

    private void Send(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _coreSession?.Write(text);
    }

    /// <summary>
    /// يكتب أمراً كاملاً وينفّذه في الجلسة الحيّة (يُستدعى من «لوحة أوامر المشروع»): يفتح كتلة استدلاليّة،
    /// يكتب الأمر متبوعاً بفاصل السطر، يلتقطه في التاريخ (T-106)، ويعيد التركيز للتيرمنال.
    /// </summary>
    public void RunCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        lock (_screenLock) _coreScreen?.BeginHeuristicCommand(command);
        _coreSession?.Write(command + _newline);
        RecordHistory(command);
        FocusTerminal();
    }

    // ===== اللصق المتقدّم + حماية اللصق (T-104.4 / T-104.5) =====

    /// <summary>
    /// يلصق نصّ الحافظة إلى الصدفة عبر مسار الحماية:
    /// النصّ متعدّد الأسطر أو الحاوي لأنماط خطرة يمرّ أوّلاً بنافذة تأكيد تعرضه كاملاً؛
    /// النصّ الآمن ذو السطر الواحد يُلصق مباشرةً. عند الوضع 2004 (Bracketed Paste)
    /// يُلَفّ النصّ بـ <c>ESC[200~ … ESC[201~</c> كي لا تفسّره الصدفة كأوامر مطبوعة.
    /// </summary>
    private void PasteFromClipboard()
    {
        string text;
        try
        {
            if (!Clipboard.ContainsText()) return;
            text = Clipboard.GetText();
        }
        catch { return; }
        if (string.IsNullOrEmpty(text)) return;

        // بوّابة الحماية: النصّ الخطر/متعدّد الأسطر يتطلّب موافقة صريحة (النافذة تعرضه كاملاً).
        if (PasteConfirmDialog.RequiresConfirmation(text))
        {
            var owner = Window.GetWindow(this);
            if (!PasteConfirmDialog.Confirm(text, owner)) return;
        }

        SendPaste(text);
    }

    /// <summary>يرسل نصّاً ملصوقاً، ملفوفاً بعلامات Bracketed Paste إن كان الوضع 2004 مُفعَّلاً.</summary>
    private void SendPaste(string text)
    {
        bool bracketed;
        lock (_screenLock) bracketed = _coreScreen?.BracketedPaste ?? false;
        if (bracketed) Send("\x1b[200~" + text + "\x1b[201~");
        else Send(text);
    }

    // ===== إبلاغ الماوس للتطبيق (SGR/X10) =====

    /// <summary>يرسل حدث ماوس مُرمَّزاً للتطبيق إن كان الإبلاغ مُفعَّلاً (يعيد true إن أُرسِل).</summary>
    private bool ReportMouse(CoreMouseButton button, CoreMouseEventType type, (int Line, int Col) cell)
    {
        if (_coreScreen == null) return false;
        int row = cell.Line - Renderer.TopLineIndex;   // الصفّ الظاهر (0-مبنيّ)
        int col = cell.Col;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        string? seq;
        lock (_screenLock)
        {
            if (!_coreScreen.MouseReportingEnabled) return false;
            seq = _coreScreen.EncodeMouse(button, type, col, row, shift, alt, ctrl);
        }
        if (seq != null) Send(seq);
        return true;
    }

    // ===== التحديد بالماوس + لصق بالزر الأيمن + عجلة التمرير/التكبير =====

    private void Renderer_MouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(Renderer);
        var cell = Renderer.CellFromPoint(pos);

        // نقرة على مِزراب الكتل (الشريط الملوّن) = تأشير الكتلة كلّها — أسرع من سحب سطورها يدوياً.
        if (cell.HasValue && Renderer.IsInBlockGutter(pos) && SelectBlockAtLine(cell.Value.Line))
        {
            Renderer.Focus();
            e.Handled = true;
            return;
        }

        // Ctrl+Click محجوز لفتح الروابط/المسارات (يُنفَّذ عند رفع الزر): لا إبلاغ ماوس ولا بدء تحديد.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Renderer.Focus();
            e.Handled = true;
            return;
        }

        if (cell.HasValue && ReportMouse(CoreMouseButton.Left, CoreMouseEventType.Press, cell.Value))
        {
            e.Handled = true;
            return;
        }

        // نقرة مزدوجة = تحديد الكلمة، ثلاثيّة = تحديد السطر (T-104.1). كلاهما ينسخ فوراً كنمط الكونسول.
        if (cell.HasValue && e.ClickCount >= 2)
        {
            Renderer.Focus();
            _selecting = false;
            _selAnchor = null;
            if (e.ClickCount == 2) SelectWordAt(cell.Value);
            else SelectLineAt(cell.Value.Line);
            if (Renderer.HasSelection)
            {
                string picked = Renderer.GetSelectedText();
                if (!string.IsNullOrEmpty(picked)) CopyToClipboard(picked);
            }
            e.Handled = true;
            return;
        }

        // بدء التحديد بالسحب.
        _selAnchor = cell;
        _selecting = _selAnchor.HasValue;
        Renderer.ClearSelection();
        Renderer.Focus();
        if (_selecting) Renderer.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>النصّ المسطّح لسطر بفهرس <c>snapshot.Lines</c> (أو "").</summary>
    private string PlainTextForLine(int lineIndex)
    {
        if (_lastSnapshot is not { } snap) return "";
        if (lineIndex < 0 || lineIndex >= snap.Lines.Count) return "";
        return LinePlainText(snap.Lines[lineIndex]);
    }

    /// <summary>هل المحرف جزء من «كلمة» للتحديد بالنقر المزدوج (أبجديّ-رقميّ + رموز مسار شائعة)؟</summary>
    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '/' || c == '\\' || c == ':' || c == '~';

    /// <summary>يحدّد حدود الكلمة عند خليّة معطاة ويضبط التحديد عليها (بالأعمدة المعروضة).</summary>
    private void SelectWordAt((int Line, int Col) cell)
    {
        string plain = PlainTextForLine(cell.Line);
        if (plain.Length == 0) { Renderer.ClearSelection(); return; }

        // العمود المعروض → فهرس نصّيّ، ثمّ توسيع على حدود الكلمة نصّياً، ثمّ الرجوع لأعمدة معروضة.
        int si = StringIndexForDisplayCol(plain, cell.Col);
        if (si >= plain.Length) si = plain.Length - 1;
        if (si < 0 || !IsWordChar(plain[si])) { Renderer.ClearSelection(); return; }

        int start = si;
        while (start > 0 && IsWordChar(plain[start - 1])) start--;
        int end = si + 1;
        while (end < plain.Length && IsWordChar(plain[end])) end++;

        int startCol = DisplayCol(plain, start);
        int endCol = DisplayCol(plain, end);
        Renderer.SetSelection((cell.Line, startCol), (cell.Line, endCol));
    }

    /// <summary>يحدّد السطر كاملاً (من العمود 0 حتى نهاية نصّه المعروض).</summary>
    private void SelectLineAt(int lineIndex)
    {
        string plain = PlainTextForLine(lineIndex);
        int endCol = DisplayCol(plain, plain.TrimEnd().Length);
        if (endCol <= 0) { Renderer.ClearSelection(); return; }
        Renderer.SetSelection((lineIndex, 0), (lineIndex, endCol));
    }

    /// <summary>يحوّل عموداً معروضاً إلى فهرس نصّيّ في السطر (عكس <see cref="DisplayCol"/>).</summary>
    private static int StringIndexForDisplayCol(string plain, int displayCol)
    {
        int col = 0;
        int i = 0;
        while (i < plain.Length)
        {
            int rune, adv;
            if (char.IsHighSurrogate(plain[i]) && i + 1 < plain.Length && char.IsLowSurrogate(plain[i + 1]))
            { rune = char.ConvertToUtf32(plain[i], plain[i + 1]); adv = 2; }
            else { rune = plain[i]; adv = 1; }
            int w = RuneWidth(rune);
            if (col + w > displayCol) return i;
            col += w;
            i += adv;
        }
        return plain.Length;
    }

    private void Renderer_MouseMove(object sender, MouseEventArgs e)
    {
        // Ctrl+Hover فوق هدف قابل للنقر ⇒ مؤشّر اليد (T-110.4)؛ وإلّا مؤشّر افتراضيّ.
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            bool overLink = (Keyboard.Modifiers & ModifierKeys.Control) != 0
                && LinkTargetAt(Renderer.CellFromPoint(e.GetPosition(Renderer))) != null;
            Renderer.Cursor = overLink ? Cursors.Hand : null;
            return;
        }

        var cell = Renderer.CellFromPoint(e.GetPosition(Renderer));
        if (cell == null) return;

        bool reportsDrag;
        lock (_screenLock) reportsDrag = _coreScreen?.MouseReportsDrag ?? false;
        if (reportsDrag && ReportMouse(CoreMouseButton.Left, CoreMouseEventType.Move, cell.Value))
            return;

        if (_selecting && _selAnchor.HasValue)
            Renderer.SetSelection(_selAnchor, cell);
    }

    private void Renderer_MouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (Renderer.IsMouseCaptured) Renderer.ReleaseMouseCapture();

        // Ctrl+Click: افتح الهدف تحت المؤشّر (رابط OSC 8 أو مسار/رابط مكتشَف)، دون تحديد أو إبلاغ.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _selecting = false;
            OpenLinkAt(Renderer.CellFromPoint(e.GetPosition(Renderer)));
            e.Handled = true;
            return;
        }

        var cell = Renderer.CellFromPoint(e.GetPosition(Renderer));
        if (cell.HasValue && ReportMouse(CoreMouseButton.Left, CoreMouseEventType.Release, cell.Value))
        {
            _selecting = false;
            e.Handled = true;
            return;
        }

        // نسخ-عند-التحديد (نمط الكونسول): يبقى التحديد ظاهراً.
        if (_selecting)
        {
            _selecting = false;
            string text = Renderer.GetSelectedText();
            if (!string.IsNullOrEmpty(text)) CopyToClipboard(text);
        }
        e.Handled = true;
    }

    /// <summary>
    /// نمط الكونسول: الزرّ الأيمن يلصق محتوى الحافظة إلى الصدفة.
    /// Shift+الزرّ الأيمن يفتح قائمة سياق الكتلة (نسخ الأمر/المخرجات/الكتلة + شرح AI).
    /// </summary>
    private void Renderer_MouseRightUp(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            var cell = Renderer.CellFromPoint(e.GetPosition(Renderer));
            long abs = cell.HasValue && _lastSnapshot is { } snap
                ? snap.BaseLine + cell.Value.Line
                : long.MaxValue;
            ShowBlockContextMenu(abs);
            e.Handled = true;
            return;
        }
        PasteFromClipboard();
        e.Handled = true;
    }

    /// <summary>
    /// سياسة عجلة التمرير القياسيّة (كانت مفقودة: كنّا نمرّر سكرول‌باكنا دائماً).
    ///
    /// داخل تطبيق كامل الشاشة لا سكرول‌باك لدينا أصلاً (اللقطة تعرض الصفوف الظاهرة فقط)، فكان
    /// <c>MaxScrollOffset = 0</c> ⇒ التمرير ميت رياضيّاً. والحلّ الصحيح ليس تمريرَ سكرول‌باكنا بل
    /// **تسليم العجلة للتطبيق** فيمرّر محتواه هو (كلود كود/less/vim يملكون تمريرهم الخاصّ):
    ///
    ///   1. Ctrl+عجلة        → تكبير/تصغير الخطّ.
    ///   2. Shift+عجلة       → تجاوز صريح: مرّر سكرول‌باكنا دائماً (منفذ الهروب القياسيّ).
    ///   3. التطبيق فعّل تعقّب الماوس → أرسل زرّ العجلة (64/65) فيمرّر محتواه.
    ///   4. شاشة بديلة بلا تعقّب ماوس → «التمرير البديل» (سلوك xterm): أرسل أسهماً فيمرّر less/man.
    ///   5. الشاشة الرئيسة   → مرّر سكرول‌باكنا محلّيّاً (السلوك السابق).
    /// </summary>
    private void Renderer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Zoom(e.Delta > 0 ? +1 : -1);
            e.Handled = true;
            return;
        }

        bool up = e.Delta > 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (!shift)
        {
            // (3) التطبيق يتعقّب الماوس ⇒ سلّمه العجلة. ReportMouse يعيد false إن كان التعقّب مطفأً.
            var cell = Renderer.CellFromPoint(e.GetPosition(Renderer));
            if (cell.HasValue &&
                ReportMouse(up ? CoreMouseButton.WheelUp : CoreMouseButton.WheelDown,
                            CoreMouseEventType.Press, cell.Value))
            {
                e.Handled = true;
                return;
            }

            // (4) شاشة بديلة بلا تعقّب ⇒ أسهم بدل العجلة (ثلاثة أسطر لكلّ نقرة، مثل xterm).
            if (_lastSnapshot?.AltScreen == true)
            {
                bool appCursor;
                lock (_screenLock) appCursor = _coreScreen?.ApplicationCursorKeys ?? false;
                string arrow = appCursor ? (up ? "\x1bOA" : "\x1bOB") : (up ? "\x1b[A" : "\x1b[B");
                Send(string.Concat(arrow, arrow, arrow));
                e.Handled = true;
                return;
            }
        }

        // (5) الشاشة الرئيسة أو Shift ⇒ سكرول‌باكنا.
        Renderer.ScrollOffset = Math.Clamp(
            Renderer.ScrollOffset + (up ? +3 : -3), 0, Renderer.MaxScrollOffset);
        if (_lastSnapshot != null) Renderer.SetSnapshot(_lastSnapshot);
        UpdateScrollBar();
        e.Handled = true;
    }

    // ===== الروابط والمسارات القابلة للنقر (Ctrl+Click) — T-110 =====

    /// <summary>
    /// يحدّد الهدف القابل للنقر عند خليّة معطاة: يُفضَّل رابط OSC 8 الصريح المخزَّن بالخليّة (T-110.5)،
    /// وإلّا يكتشف بالـRegex على نصّ السطر المنقور فقط (T-110.1/.2). يعيد <c>null</c> إن لا هدف.
    /// </summary>
    private Services.LinkTarget? LinkTargetAt((int Line, int Col)? cell)
    {
        if (cell is not { } c) return null;
        if (_lastSnapshot is not { } snap) return null;
        if (c.Line < 0 || c.Line >= snap.Lines.Count) return null;

        var spans = snap.Lines[c.Line];

        // T-110.5: رابط OSC 8 صريح تحت العمود ⇒ أولويّة مطلقة.
        string? explicitLink = HyperlinkAtColumn(spans, c.Col);
        if (!string.IsNullOrEmpty(explicitLink))
            return new Services.LinkTarget(Services.LinkTargetKind.Url, explicitLink, 0, 0);

        // T-110.1/.2: كشف بالـRegex على نصّ هذا السطر وحده.
        string plain = LinePlainText(spans);
        int strIndex = ColumnToStringIndex(spans, c.Col);
        return Services.LinkDetector.Detect(plain, strIndex);
    }

    /// <summary>ينفّذ فتح الهدف القابل للنقر عند الخليّة (رابط OSC 8 صريح أو مسار/رابط مكتشَف).</summary>
    private void OpenLinkAt((int Line, int Col)? cell)
    {
        if (cell is not { } c || _lastSnapshot is not { } snap) return;
        if (c.Line < 0 || c.Line >= snap.Lines.Count) return;

        var spans = snap.Lines[c.Line];

        // رابط OSC 8 صريح: عنوان ويب/mailto يُفتح مباشرةً؛ مسار نظام موجود يعرض القائمة الطائرة (T-7).
        string? explicitLink = HyperlinkAtColumn(spans, c.Col);
        if (!string.IsNullOrEmpty(explicitLink))
        {
            if (ResolveExistingPath(explicitLink) is { } fsPath)
                ShowPathMenu(fsPath);
            else
                Services.LinkOpener.OpenExplicit(explicitLink);
            return;
        }

        // وإلّا: اكتشف بالـRegex. المسار الموجود ⇒ قائمة طائرة؛ الرابط أو مسار غير موجود ⇒ السلوك القديم.
        string plain = LinePlainText(spans);
        int strIndex = ColumnToStringIndex(spans, c.Col);
        if (Services.LinkDetector.Detect(plain, strIndex) is not { } target) return;

        if (target.Kind == Services.LinkTargetKind.Path && ResolvePath(target.Value) is { } path)
            ShowPathMenu(path);
        else
            Services.LinkOpener.Open(target);
    }

    // ===== القائمة الطائرة لفتح المسار + محرّر الملفّ داخل التاب (T-7) =====

    /// <summary>
    /// يحلّ مساراً (مطلقاً أو نسبيّاً لمجلّد عمل الجزء) إلى مسار مطلق إن كان موجوداً على القرص،
    /// وإلّا <c>null</c> (فنُبقي السلوك القديم لغير المسارات).
    /// </summary>
    private string? ResolvePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string raw = value.Trim();
        try
        {
            // مسار مطلق كما هو؛ نسبيّ يُحَلّ مقابل مجلّد عمل الجزء إن توفّر.
            string full = System.IO.Path.IsPathRooted(raw)
                ? System.IO.Path.GetFullPath(raw)
                : System.IO.Path.GetFullPath(raw,
                    string.IsNullOrWhiteSpace(_entry.Path) ? Environment.CurrentDirectory : _entry.Path!);
            if (System.IO.File.Exists(full) || System.IO.Directory.Exists(full)) return full;
        }
        catch { }
        return null;
    }

    /// <summary>يحلّ رابط OSC 8 صريح (قد يكون <c>file://</c>) إلى مسار نظام موجود، أو <c>null</c>.</summary>
    private string? ResolveExistingPath(string uri)
    {
        uri = uri.Trim();
        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(uri, UriKind.Absolute, out var fileUri))
            return ResolvePath(fileUri.LocalPath);
        if (uri.Contains("://")) return null;   // مخطّط آخر (http/mailto/…) ⇒ ليس مساراً
        return ResolvePath(uri);
    }

    /// <summary>
    /// يعرض قائمة سياق صغيرة عند مؤشّر الفأرة بخيارَي «فتح المجلّد» و«فتح الملفّ»
    /// (الأخير مفعَّل للملفّات فقط). كلّها معرَّبة.
    /// </summary>
    private void ShowPathMenu(string path)
    {
        bool isFile = System.IO.File.Exists(path);
        var menu = new ContextMenu { FlowDirection = Services.Loc.Flow, PlacementTarget = Renderer };

        var openFolder = MenuItemFor(Services.Loc.T("menu.openFolder"), () => OpenInExplorer(path, isFile));
        menu.Items.Add(openFolder);

        var openFile = MenuItemFor(Services.Loc.T("menu.openFile"), () => OpenFileInEditor(path));
        openFile.IsEnabled = isFile;
        menu.Items.Add(openFile);

        menu.IsOpen = true;
    }

    /// <summary>يفتح المجلّد في Explorer؛ لملفّ يفتح مجلّده الحاوي محدِّداً الملفّ (<c>/select,</c>).</summary>
    private static void OpenInExplorer(string path, bool isFile)
    {
        try
        {
            if (isFile)
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            else
                System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch { }
    }

    /// <summary>يفتح ملفّاً في لوحة المحرّر داخل التاب: يُظهر عمود المحرّر بنصف العرض ويحمّل الملفّ.</summary>
    private void OpenFileInEditor(string path)
    {
        EditorColumn.Width = new GridLength(1, GridUnitType.Star);   // ~نصف العرض
        SplitterColumn.Width = GridLength.Auto;
        EditorSplitter.Visibility = Visibility.Visible;
        EditorPanel.Visibility = Visibility.Visible;
        EditorPanel.Open(path);
    }

    /// <summary>يطوي عمود المحرّر ويعيد التيرمنال إلى كامل العرض (يُستدعى عند إغلاق اللوحة).</summary>
    private void CollapseEditor()
    {
        EditorPanel.Visibility = Visibility.Collapsed;
        EditorSplitter.Visibility = Visibility.Collapsed;
        EditorColumn.Width = new GridLength(0);
        Renderer.Focus();
    }

    /// <summary>
    /// يعيد رابط OSC 8 المخزَّن بالخليّة عند العمود المعروض <paramref name="col"/> (أو <c>null</c>)،
    /// بالمشي على المقاطع محترماً الأحرف العريضة (خليّتان).
    /// </summary>
    private static string? HyperlinkAtColumn(FrozenSpan[] spans, int col)
    {
        int c = 0;
        foreach (var span in spans)
        {
            int w = SpanDisplayWidth(span.Text);
            if (col >= c && col < c + w)
                return span.Hyperlink;
            c += w;
        }
        return null;
    }

    /// <summary>يحوّل عموداً معروضاً إلى فهرس محرف في نصّ السطر المسطّح (يحترم الأحرف العريضة).</summary>
    private static int ColumnToStringIndex(FrozenSpan[] spans, int col)
    {
        int displayCol = 0;
        int strIndex = 0;
        foreach (var span in spans)
        {
            string text = span.Text;
            int i = 0;
            while (i < text.Length)
            {
                int rune, adv;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                { rune = char.ConvertToUtf32(text[i], text[i + 1]); adv = 2; }
                else { rune = text[i]; adv = 1; }

                if (displayCol >= col) return strIndex;
                displayCol += RuneWidth(rune);
                strIndex += adv;
                i += adv;
            }
        }
        return strIndex;
    }

    /// <summary>مجموع الأعمدة المعروضة لنصّ مقطع (يحترم الأحرف العريضة).</summary>
    private static int SpanDisplayWidth(string text)
    {
        int w = 0;
        int i = 0;
        while (i < text.Length)
        {
            int rune, adv;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            { rune = char.ConvertToUtf32(text[i], text[i + 1]); adv = 2; }
            else { rune = text[i]; adv = 1; }
            w += RuneWidth(rune);
            i += adv;
        }
        return w;
    }

    // ===== نموذج الكتل (نمط Warp): تنقّل + نسخ + شرح AI =====

    /// <summary>يقفز لبداية الكتلة السابقة (dir=-1) أو التالية (dir=+1) عبر إزاحات العارض.</summary>
    private void JumpBlock(int dir)
    {
        if (_lastSnapshot is not { } snap || snap.AltScreen) return;   // لا كتل في تطبيق شاشة كاملة
        if (snap.Blocks.Count == 0) return;

        // السطر المطلق للصفّ الأعلى ظاهراً حاليّاً (مرجع القفز النسبيّ).
        long viewAbs = snap.BaseLine + Renderer.TopLineIndex;
        long target = -1;
        if (dir < 0)
        {
            for (int i = snap.Blocks.Count - 1; i >= 0; i--)
                if (snap.Blocks[i].StartLine < viewAbs) { target = snap.Blocks[i].StartLine; break; }
            if (target < 0) target = snap.Blocks[0].StartLine;
        }
        else
        {
            for (int i = 0; i < snap.Blocks.Count; i++)
                if (snap.Blocks[i].StartLine > viewAbs) { target = snap.Blocks[i].StartLine; break; }
            if (target < 0) target = snap.Blocks[^1].StartLine;
        }

        int lineIndex = (int)(target - snap.BaseLine);
        ScrollLineIntoView(lineIndex);
    }

    /// <summary>الكتلة التي تحتوي السطر المطلق المعطى (أو null).</summary>
    private BlockSnapshot? BlockAtAbsLine(long abs)
    {
        if (_lastSnapshot is not { } snap || snap.AltScreen) return null;
        BlockSnapshot? found = null;
        foreach (var b in snap.Blocks)
        {
            long end = b.EndLine == long.MaxValue ? long.MaxValue : b.EndLine;
            if (abs >= b.StartLine && abs < end) found = b;
        }
        return found;
    }

    /// <summary>يستخرج نصّاً مسطّحاً لمدى أسطر [from,to) من آخر لقطة.</summary>
    private string ExtractLines(long from, long to)
    {
        if (_lastSnapshot is not { } snap) return "";
        long end = to == long.MaxValue ? snap.BaseLine + snap.Lines.Count : to;
        var sb = new StringBuilder();
        for (long abs = from; abs < end; abs++)
        {
            int idx = (int)(abs - snap.BaseLine);
            if (idx < 0 || idx >= snap.Lines.Count) continue;
            foreach (var s in snap.Lines[idx]) sb.Append(s.Text);
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    private string BlockCommandText(BlockSnapshot b)
        => !string.IsNullOrWhiteSpace(b.CommandText) ? b.CommandText : ExtractLines(b.StartLine, b.OutputStartLine);

    private string BlockOutputText(BlockSnapshot b) => ExtractLines(b.OutputStartLine, b.EndLine);

    private string BlockFullText(BlockSnapshot b) => ExtractLines(b.StartLine, b.EndLine);

    private static void CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text ?? ""); } catch { }
    }

    /// <summary>
    /// يؤشّر الكتلة التي يقع فيها السطر المعطى (فهرس داخل <c>snapshot.Lines</c>) كاملةً — من سطر
    /// الأمر إلى آخر سطر مخرجات. يعيد false إن لا كتلة هناك (شاشة بديلة أو خارج أيّ كتلة).
    /// </summary>
    private bool SelectBlockAtLine(int lineIndex)
    {
        if (_lastSnapshot is not { } snap || snap.AltScreen) return false;
        if (BlockAtAbsLine(snap.BaseLine + lineIndex) is not { } b) return false;

        int start = (int)(b.StartLine - snap.BaseLine);
        long endAbs = b.EndLine == long.MaxValue ? snap.BaseLine + snap.Lines.Count : b.EndLine;
        int end = (int)(endAbs - snap.BaseLine) - 1;          // آخر سطر داخل الكتلة (شامل)
        if (end < start) return false;

        start = Math.Max(0, start);
        end = Math.Min(end, snap.Lines.Count - 1);
        Renderer.SetSelection((start, 0), (end, PlainTextForLine(end).Length));
        return true;
    }

    private void CopyCurrentBlock()
    {
        // الكتلة عند أعلى الصفّ الظاهر (لا caret مع عارض Skia).
        if (_lastSnapshot is not { } snap) return;
        long abs = snap.BaseLine + Renderer.TopLineIndex;
        if (BlockAtAbsLine(abs) is { } b) CopyToClipboard(BlockFullText(b));
    }

    /// <summary>يستدعي مساعد الـ AI لشرح الكتلة (يظهر الناتج/التلميح في مربّع رسالة).</summary>
    private async void ExplainBlock(BlockSnapshot b)
    {
        var ctx = new Services.AiBlockContext
        {
            CommandText = BlockCommandText(b),
            Output = BlockOutputText(b),
            ExitCode = b.ExitCode,
        };
        string result;
        try { result = await _ai.SuggestAsync(ctx); }
        catch (Exception ex) { result = "تعذّر تشغيل مساعد الـ AI: " + ex.Message; }
        TerminalLauncher.Views.AppDialog.Alert(Window.GetWindow(this),
            _ai.IsEnabled ? "شرح الكتلة" : "مساعد الـ AI (اختياريّ)", result);
    }

    /// <summary>يفتح قائمة سياق الكتلة (Shift+زر أيمن) بأفعال النسخ والشرح للكتلة عند السطر المطلق المعطى.</summary>
    private void ShowBlockContextMenu(long abs)
    {
        if (BlockAtAbsLine(abs) is not { } b)
        {
            TerminalLauncher.Views.AppDialog.Alert(Window.GetWindow(this), "الكتل", "لا توجد كتلة عند هذا الموضع.");
            return;
        }
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        menu.Items.Add(MenuItemFor("نسخ الأمر", () => CopyToClipboard(BlockCommandText(b))));
        menu.Items.Add(MenuItemFor("نسخ المخرجات", () => CopyToClipboard(BlockOutputText(b))));
        menu.Items.Add(MenuItemFor("نسخ الكتلة", () => CopyToClipboard(BlockFullText(b))));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("اشرح هذه الكتلة (AI)", () => ExplainBlock(b)));
        menu.PlacementTarget = Renderer;
        menu.IsOpen = true;
    }

    private static MenuItem MenuItemFor(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    // ===== تكبير/تصغير الخط =====

    private void Zoom(int steps)
    {
        double size = Math.Clamp(_fontSize + steps, MinFontSize, MaxFontSize);
        if (Math.Abs(size - _fontSize) < 0.01) return;
        ApplyFontSize(size);
        _persistFontSize?.Invoke(size);
    }

    private void ResetZoom()
    {
        if (Math.Abs(_fontSize - DefaultFontSize) < 0.01) return;
        ApplyFontSize(DefaultFontSize);
        _persistFontSize?.Invoke(DefaultFontSize);
    }

    private void ApplyFontSize(double size)
    {
        _fontSize = size;
        Renderer.TerminalFontSize = size;
        MarkDirty();          // يعيد الرسم من نموذج الشاشة
        ResizeSession();         // الأعمدة/الأسطر تتبع حجم الخط
    }

    // ===== البحث في المخرجات (Ctrl+F) — على اللقطة =====

    private void OpenSearch()
    {
        SearchBar.Visibility = Visibility.Visible;
        SearchInput.Focus();
        SearchInput.SelectAll();
        if (SearchInput.Text.Length > 0) RunSearch();
    }

    private void SearchClose_Click(object sender, RoutedEventArgs e) => CloseSearch();

    private void CloseSearch()
    {
        SearchBar.Visibility = Visibility.Collapsed;
        Renderer.ClearSearchMatches();
        _matches.Clear();
        _matchIndex = -1;
        Renderer.Focus();
    }

    private void SearchInput_TextChanged(object sender, TextChangedEventArgs e) => RunSearch();

    private void SearchInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: CloseSearch(); e.Handled = true; break;
            case Key.Enter:
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) StepMatch(-1);
                else StepMatch(+1);
                e.Handled = true;
                break;
        }
    }

    private void SearchNext_Click(object sender, RoutedEventArgs e) => StepMatch(+1);
    private void SearchPrev_Click(object sender, RoutedEventArgs e) => StepMatch(-1);

    /// <summary>يحسب المطابقات من اللقطة الحاليّة ويُبرِز الأولى.</summary>
    private void RunSearch()
    {
        string term = SearchInput.Text;
        _matches = ComputeMatches(term);
        if (_matches.Count == 0)
        {
            _matchIndex = -1;
            Renderer.ClearSearchMatches();
            SearchCount.Text = term.Length == 0 ? "" : "لا نتائج";
            return;
        }
        _matchIndex = 0;
        ShowCurrentMatch();
    }

    /// <summary>ينتقل للمطابقة التالية/السابقة (يعيد الحساب لضمان طزاجة الفهارس مع الإخراج الحيّ).</summary>
    private void StepMatch(int dir)
    {
        string term = SearchInput.Text;
        if (term.Length == 0) return;
        _matches = ComputeMatches(term);
        if (_matches.Count == 0)
        {
            _matchIndex = -1;
            Renderer.ClearSearchMatches();
            SearchCount.Text = "لا نتائج";
            return;
        }
        if (_matchIndex < 0) _matchIndex = 0;
        _matchIndex = (_matchIndex + dir + _matches.Count) % _matches.Count;
        ShowCurrentMatch();
    }

    /// <summary>يُبرِز المطابقة الحاليّة، يمرّرها إلى العرض، ويحدّث عدّاد النتائج.</summary>
    private void ShowCurrentMatch()
    {
        if (_matchIndex < 0 || _matchIndex >= _matches.Count) return;
        ScrollLineIntoView(_matches[_matchIndex].Line);
        Renderer.SetSearchMatches(_matches, _matchIndex);
        SearchCount.Text = $"{_matchIndex + 1}/{_matches.Count}";
    }

    /// <summary>
    /// يحسب مطابقات المصطلح على أسطر اللقطة: لكل سطر يبني نصّه المسطّح، يجد المواضع (بلا حساسية لحالة الأحرف)،
    /// ثم يحوّل كل (فهرس نصّي + طول) إلى (عمود معروض + طول معروض) مراعاةً للأحرف العريضة.
    /// </summary>
    private List<(int Line, int StartCol, int Length)> ComputeMatches(string term)
    {
        var result = new List<(int, int, int)>();
        if (_lastSnapshot is not { } snap || string.IsNullOrEmpty(term)) return result;

        for (int li = 0; li < snap.Lines.Count; li++)
        {
            string plain = LinePlainText(snap.Lines[li]);
            if (plain.Length == 0) continue;

            int from = 0;
            while (from <= plain.Length - term.Length)
            {
                int idx = plain.IndexOf(term, from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                int startCol = DisplayCol(plain, idx);
                int endCol = DisplayCol(plain, idx + term.Length);
                result.Add((li, startCol, Math.Max(1, endCol - startCol)));
                from = idx + Math.Max(1, term.Length);
            }
        }
        return result;
    }

    /// <summary>نصّ السطر المسطّح (سَلسَلة نصوص المقاطع).</summary>
    private static string LinePlainText(FrozenSpan[] spans)
    {
        if (spans.Length == 0) return "";
        var sb = new StringBuilder();
        foreach (var s in spans) sb.Append(s.Text);
        return sb.ToString();
    }

    /// <summary>يحوّل فهرساً نصّياً في سطر إلى عمود معروض (الأحرف العريضة تتقدّم عمودين).</summary>
    private static int DisplayCol(string plain, int stringIndex)
    {
        int col = 0;
        int i = 0;
        while (i < plain.Length && i < stringIndex)
        {
            int rune;
            int adv;
            if (char.IsHighSurrogate(plain[i]) && i + 1 < plain.Length && char.IsLowSurrogate(plain[i + 1]))
            {
                rune = char.ConvertToUtf32(plain[i], plain[i + 1]);
                adv = 2;
            }
            else { rune = plain[i]; adv = 1; }
            col += RuneWidth(rune);
            i += adv;
        }
        return col;
    }

    /// <summary>عرض نقطة الترميز بالخلايا (نسخة محلّيّة تطابق <see cref="SkiaTerminalRenderer"/>).</summary>
    private static int RuneWidth(int r)
    {
        if (r < 0x1100) return 1;
        if ((r >= 0x1100 && r <= 0x115F) ||
            (r >= 0x2E80 && r <= 0xA4CF) ||
            (r >= 0x3000 && r <= 0x303E) ||
            (r >= 0x3041 && r <= 0x33FF) ||
            (r >= 0x3400 && r <= 0x4DBF) ||
            (r >= 0x4E00 && r <= 0x9FFF) ||
            (r >= 0xAC00 && r <= 0xD7A3) ||
            (r >= 0xF900 && r <= 0xFAFF) ||
            (r >= 0xFF00 && r <= 0xFF60) ||
            (r >= 0xFFE0 && r <= 0xFFE6) ||
            (r >= 0x1F300 && r <= 0x1FAFF) ||
            (r >= 0x20000 && r <= 0x3FFFD))
            return 2;
        return 1;
    }

    /// <summary>يضبط إزاحة التمرير كي يظهر سطر (بفهرس snapshot.Lines) ضمن نافذة العرض، ثم يعيد الرسم.</summary>
    private void ScrollLineIntoView(int lineIndex)
    {
        if (_lastSnapshot == null) return;
        int rows = Math.Max(1, Renderer.VisibleRows);
        int total = Renderer.TotalLines;

        // الإزاحة من القاع = total - rows - top، حيث top هو أعلى صفّ ظاهر.
        // نضع السطر المستهدف قرب أعلى النافذة (مع هامش صغير إن أمكن).
        int desiredTop = Math.Clamp(lineIndex - 1, 0, Math.Max(0, total - rows));
        int offset = Math.Clamp(total - rows - desiredTop, 0, Renderer.MaxScrollOffset);

        // إن كان السطر ظاهراً أصلاً لا نحرّك (نتجنّب قفزات مزعجة).
        int curTop = Renderer.TopLineIndex;
        if (lineIndex < curTop || lineIndex >= curTop + rows)
        {
            Renderer.ScrollOffset = offset;
            Renderer.SetSnapshot(_lastSnapshot);
            UpdateScrollBar();
        }
    }

    /// <summary>
    /// يطبّق تفضيلات الخطّ من الإعدادات (نوع + حجم) على العارض ثم يعيد ضبط أبعاد الجلسة.
    /// </summary>
    public void ApplyFontSettings(double fontSize, string fontFamily)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            try
            {
                Renderer.TerminalFontFamily = fontFamily.Contains('#')
                    // خطّ مضمّن (مورد): يُحلَّل عبر URI أساس التطبيق (الشكل الموثَّق للموارد).
                    ? new System.Windows.Media.FontFamily(new Uri("pack://application:,,,/"), fontFamily)
                    // خطّ نظام: أضِف Consolas احتياطياً لضمان أحاديّة المسافة إن لم يكن مُنصَّباً.
                    : new System.Windows.Media.FontFamily(fontFamily + ", Consolas");
            }
            catch { }
        }
        ApplyFontSize(Math.Clamp(fontSize, MinFontSize, MaxFontSize));
    }

    /// <summary>
    /// يضبط شفافيّة خلفيّة التيرمنال (يُستدعى من النافذة عند تفعيل/تغيير صورة الخلفيّة أو منزلق الشفافيّة):
    /// alpha &lt; 1 ⇒ صورة نشطة، فنجعل خلفيّة العارض شبه شفّافة ونُشفِّف الطبقات خلفه (شبكة الجذر) لتظهر
    /// صورة النافذة؛ alpha = 1 ⇒ لا صورة، فنُعيد خلفيّة التيرمنال المعتِمة من الثيم.
    /// </summary>
    public void SetBackgroundAlpha(double alpha)
    {
        Renderer.BackgroundAlpha = alpha;
        bool imageActive = alpha < 0.9999;
        if (imageActive)
        {
            RootGrid.Background = System.Windows.Media.Brushes.Transparent;
            // خلفيّة الشريط العلويّ والإنبت = نفس تعتيم التيرمنال (لون خلفيّته بشفافيّة alpha) كي تبدو
            // ثلاثتها قطعةً واحدة فوق الصورة؛ بلا هذا تُظهر الصورةَ كاملة السطوع فتبدو بنلاً مختلفاً.
            var c = global::TerminalLauncher.Terminal.AnsiPalette.BackgroundColor;
            ComposerBar.Background = new System.Windows.Media.SolidColorBrush(c) { Opacity = alpha };
            HeaderBar.Background = new System.Windows.Media.SolidColorBrush(c) { Opacity = alpha };
        }
        else
        {
            RootGrid.SetResourceReference(System.Windows.Controls.Grid.BackgroundProperty, "Brush.TerminalBg");
            ComposerBar.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Brush.TerminalBg");
            HeaderBar.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Brush.TerminalBg");
        }
    }

    /// <summary>يمنح منطقة الإخراج تركيز لوحة المفاتيح (يُستدعى عند تفعيل الجزء).</summary>
    /// <summary>يركّز مدخل الكتابة: صندوق التأليف إن كان نشطاً، وإلّا شبكة التيرمنال.</summary>
    public void FocusTerminal()
    {
        if (ComposerActive) ComposerInput.Focus();
        else Renderer.Focus();
    }

    /// <summary>يعيد المؤشّر إلى الطور الظاهر ويُعيد تشغيل مؤقّت الوميض (يُستدعى مع كل ضغطة مفتاح).</summary>
    private void ResetCursorBlink()
    {
        Renderer.CursorBlinkOn = true;
        if (_blinkTimer.IsEnabled) { _blinkTimer.Stop(); _blinkTimer.Start(); }
    }

    /// <summary>
    /// يُنادى عند إغلاق التاب/النافذة لإنهاء الجلسة. <paramref name="deleteHistory"/> = true حين يغلق
    /// المستخدم الجلسة صراحةً (يمسح تاريخها من التخزين)؛ = false عند إغلاق التطبيق (يُبقيه للاسترجاع).
    /// </summary>
    public void CloseSession(bool deleteHistory = false)
    {
        _refresh.Stop();
        _statusTimer.Stop();
        _blinkTimer.Stop();
        _coreSession?.Dispose();
        _coreSession = null;
        if (deleteHistory)
            try { _sessionHistory.DeleteSession(SessionId); } catch { }
    }
}
