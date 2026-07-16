using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TerminalLauncher.Views;

namespace TerminalLauncher.Services;

/// <summary>
/// شبكة الأمان الأخيرة للأخطاء غير المتوقّعة: يلتقط كلّ استثناء لم يُعالَج في التطبيق، ويقيّده في
/// ملفّ سجلّ متدحرج تحت <c>%AppData%\HeliumRedTools\TerminalLauncher\logs</c>، ويُعلم المستخدم بحوار
/// الثيم مع إمكانيّة فتح السجلّ — بدل الانهيار الصامت بلا أثر.
///
/// ثلاثة مصادر تُلتقط عبر <see cref="Install"/>:
///   • <c>DispatcherUnhandledException</c> — خطأ خيط الواجهة. يُعلَّم <c>Handled</c> فيبقى التطبيق حيّاً + حوار.
///   • <c>AppDomain.UnhandledException</c> — خطأ قاتل على خيط آخر؛ لا سبيل للتعافي: نسجّل ونُخطر فقط.
///   • <c>TaskScheduler.UnobservedTaskException</c> — خطأ مهمّة لم يُراقَب؛ يُعلَّم مُراقَباً ويُسجَّل بصمت.
///
/// قاعدتان حاكمتان: التسجيل نفسه لا يرمي أبداً (كلّ شيء مغلَّف)، والحوار لا يمنع التسجيل إن فشل.
/// </summary>
public static class CrashReporter
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeliumRedTools", "TerminalLauncher", "logs");

    /// <summary>مسار ملفّ السجلّ الحاليّ (عامّ كي تفتحه الواجهة من زرّ «فتح ملفّ السجلّ»).</summary>
    public static string LogPath { get; } = Path.Combine(Dir, "app.log");

    /// <summary>مسار السجلّ السابق بعد التدحرج — نحتفظ بنسخة واحدة فقط.</summary>
    public static string PrevLogPath { get; } = Path.Combine(Dir, "app.prev.log");

    /// <summary>سقف حجم السجلّ (~1 م.ب) — عند تجاوزه يُنقَل إلى <see cref="PrevLogPath"/> ويُبدأ ملفّ جديد.</summary>
    private const long MaxBytes = 1024 * 1024;

    /// <summary>أقصر مهلة بين حوارَي خطأ متتاليين — درءاً لعاصفة حوارات إن تكرّر الخطأ في حلقة.</summary>
    private static readonly TimeSpan DialogCooldown = TimeSpan.FromSeconds(10);

    private static readonly object FileGate = new();     // يحرس الكتابة/التدحرج (خيوط متعدّدة)
    private static readonly object DialogGate = new();   // يحرس مانع عاصفة الحوارات

    private static bool _installed;
    private static bool _dialogOpen;
    private static DateTime _lastDialogUtc = DateTime.MinValue;

    /// <summary>
    /// يربط ملتقطات الاستثناءات الثلاثة. يُستدعى مرّة واحدة باكراً في <c>OnStartup</c> (بعد كتلة
    /// إعادة الإطلاق المنفصلة — كي لا يُركَّب في العمليّة التي تخرج فوراً).
    /// </summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        try
        {
            if (Application.Current != null)
                Application.Current.DispatcherUnhandledException += OnDispatcherException;

            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }
        catch { /* إن تعذّر الربط فلا نُسقط الإقلاع بسببه */ }
    }

    // ── الملتقطات ─────────────────────────────────────────────────────────────

    /// <summary>خطأ على خيط الواجهة: نسجّله ونمتصّه (يبقى التطبيق حيّاً) ثمّ نعرض حواراً غير مانع.</summary>
    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception, "Dispatcher");
        e.Handled = true;   // امتصّ الخطأ: نافذة/زرّ واحد قد يتعطّل، لكنّ التطبيق لا ينهار
        ShowNonFatalDialog();
    }

    /// <summary>خطأ قاتل خارج خيط الواجهة: التسجيل أوّلاً (فالعمليّة على وشك الموت) ثمّ إخطار بأفضل جهد.</summary>
    private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception
                 ?? new Exception(e.ExceptionObject?.ToString() ?? "(unknown non-Exception throw)");
        Log(ex, e.IsTerminating ? "AppDomain (fatal)" : "AppDomain");
        ShowFatalDialog();
    }

    /// <summary>استثناء مهمّة لم يُراقَب: يُعلَّم مُراقَباً (وإلّا أسقط العمليّة) ويُسجَّل بلا حوار — غالباً ضجيج خلفيّ.</summary>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log(e.Exception, "TaskScheduler");
        e.SetObserved();
    }

    // ── التسجيل ───────────────────────────────────────────────────────────────

    /// <summary>
    /// يُلحق مدخلة مؤرَّخة بالسجلّ: الطابع الزمنيّ (ISO) + الإصدار + المصدر + نوع الاستثناء ورسالته
    /// وتتبّع المكدّس، متدرّجاً في الاستثناءات الداخليّة. لا يرمي أبداً مهما حدث.
    /// </summary>
    public static void Log(Exception ex, string source)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("═══ ")
              // ثابت الثقافة: لا أرقام هنديّة ولا تقويم هجريّ في السجلّ مهما كانت لغة الواجهة
              .Append(DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture))
              .Append(" · v").Append(AppVersion.Current)
              .Append(" · ").Append(source)
              .AppendLine(" ═══");
            AppendException(sb, ex, 0);
            sb.AppendLine();
            Write(sb.ToString());
        }
        catch { /* التسجيل نفسه لا يجوز أن يرمي — وإلّا صار هو مصدر الانهيار */ }
    }

    /// <summary>يكتب الاستثناء وسلسلة الاستثناءات الداخليّة (مع تفريع <c>AggregateException</c>) بإزاحة حسب العمق.</summary>
    private static void AppendException(StringBuilder sb, Exception ex, int depth)
    {
        if (depth > 6) { sb.AppendLine("  … (تجاوز عمق التداخل)"); return; }

        string pad = new string(' ', depth * 2);
        sb.Append(pad).Append(ex.GetType().FullName).Append(": ").AppendLine(ex.Message);
        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            sb.AppendLine(ex.StackTrace);

        if (ex is AggregateException agg)
        {
            // InnerException لـ Aggregate هو أوّل InnerExceptions — نمرّ على القائمة وحدها بلا تكرار
            foreach (var inner in agg.InnerExceptions)
            {
                sb.Append(pad).AppendLine("└─ داخليّ:");
                AppendException(sb, inner, depth + 1);
            }
        }
        else if (ex.InnerException != null)
        {
            sb.Append(pad).AppendLine("└─ داخليّ:");
            AppendException(sb, ex.InnerException, depth + 1);
        }
    }

    /// <summary>كتابة متزامنة (قفل) — قد تُسجّل خيوط متعدّدة في اللحظة نفسها.</summary>
    private static void Write(string text)
    {
        lock (FileGate)
        {
            Directory.CreateDirectory(Dir);
            Roll();
            File.AppendAllText(LogPath, text, Encoding.UTF8);
        }
    }

    /// <summary>تدحرج السجلّ عند تجاوز السقف: السابق يُحذف، والحاليّ يصير السابق، ويُبدأ ملفّ نظيف.</summary>
    private static void Roll()
    {
        try
        {
            var fi = new FileInfo(LogPath);
            if (!fi.Exists || fi.Length < MaxBytes) return;
            if (File.Exists(PrevLogPath)) File.Delete(PrevLogPath);
            File.Move(LogPath, PrevLogPath);
        }
        catch { /* التدحرج ليس حرِجاً: إن فشل نتابع الإلحاق بالملفّ الحاليّ */ }
    }

    /// <summary>يفتح السجلّ بالبرنامج الافتراضيّ للنظام (عامّ — يستدعيه زرّ «فتح ملفّ السجلّ» وأيّ لوحة إعدادات).</summary>
    public static void OpenLog()
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(LogPath, "", Encoding.UTF8);
            }
            Process.Start(new ProcessStartInfo(LogPath) { UseShellExecute = true });
        }
        catch { /* لا مُعالِج مسجَّل أو مُنع الفتح — غير حرِج */ }
    }

    // ── الحوار ────────────────────────────────────────────────────────────────

    /// <summary>يعرض حوار الخطأ غير القاتل مؤجَّلاً على خيط الواجهة (كي يُفرَّغ ملتقط الاستثناء أوّلاً).</summary>
    private static void ShowNonFatalDialog()
    {
        if (!TryReserveDialog()) return;   // حوار مفتوح أو ضمن مهلة التهدئة ⇒ سُجِّل الخطأ ويكفي

        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted || disp.HasShutdownFinished)
            {
                ReleaseDialog();
                return;
            }

            disp.BeginInvoke(new Action(() =>
            {
                try { ShowDialog(Loc.T("crash.message")); }
                catch (Exception ex) { Log(ex, "CrashReporter.Dialog"); }
                finally { ReleaseDialog(); }
            }), DispatcherPriority.Normal);
        }
        catch
        {
            ReleaseDialog();   // فشل الجدولة — حرّر الحجز كي لا يُقفَل الحوار للأبد
        }
    }

    /// <summary>يعرض حوار الخطأ القاتل بأفضل جهد قبل موت العمليّة (مانع — كي يراه المستخدم فعلاً).</summary>
    private static void ShowFatalDialog()
    {
        if (!TryReserveDialog()) return;

        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted || disp.HasShutdownFinished) return;
            disp.Invoke(() => ShowDialog(Loc.T("crash.fatal")), DispatcherPriority.Send);
        }
        catch { /* الخيط ميت أو الحوار تعذّر — السجلّ كُتِب وهو الأهمّ */ }
        finally { ReleaseDialog(); }
    }

    /// <summary>حوار الثيم: زرّ محايد يفتح السجلّ وزرّ لكنة للمتابعة.</summary>
    private static void ShowDialog(string message)
    {
        string? key = AppDialog.Confirm(OwnerWindow(), Loc.T("crash.title"), message,
            (Loc.T("crash.openLog"), "openLog", DialogButtonKind.Neutral),
            (Loc.T("crash.continue"), "continue", DialogButtonKind.Accent));

        if (key == "openLog") OpenLog();
    }

    /// <summary>النافذة الرئيسة مالكاً — فقط إن كانت محمَّلة ومرئيّة، وإلّا بلا مالك (وسط الشاشة).</summary>
    private static Window? OwnerWindow()
    {
        try
        {
            var w = Application.Current?.MainWindow;
            return w != null && w.IsLoaded && w.IsVisible ? w : null;
        }
        catch { return null; }
    }

    /// <summary>يحجز حقّ عرض حوار: يفشل إن كان حوار مفتوحاً أو لم تنقضِ مهلة التهدئة.</summary>
    private static bool TryReserveDialog()
    {
        lock (DialogGate)
        {
            if (_dialogOpen) return false;
            if (DateTime.UtcNow - _lastDialogUtc < DialogCooldown) return false;
            _dialogOpen = true;
            return true;
        }
    }

    /// <summary>يحرّر الحجز ويبدأ مهلة التهدئة من لحظة إغلاق الحوار (لا من لحظة فتحه).</summary>
    private static void ReleaseDialog()
    {
        lock (DialogGate)
        {
            _dialogOpen = false;
            _lastDialogUtc = DateTime.UtcNow;
        }
    }
}
