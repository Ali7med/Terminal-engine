using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace TerminalLauncher.Services;

/// <summary>
/// التحديث التلقائيّ الصامت عبر Velopack، ومصدره إصدارات GitHub العامّة للمستودع.
///
/// السياسة المعتمَدة: فحص صامت عند الإقلاع ← تنزيل في الخلفيّة ← إشعار «تحديث جاهز» ←
/// <b>يُطبَّق عند إعادة التشغيل</b>. لا نُعيد التشغيل قسراً ولا نسأل المستخدم عنه؛ التحديث
/// يُركَّب بهدوء بعد خروجه من التطبيق متى شاء.
///
/// قاعدتان حاكمتان:
///   • <b>لا ترمي أبداً</b> — كلّ مسار شبكيّ مغلَّف، وأيّ فشل يُسجَّل بصمت عبر
///     <see cref="CrashReporter.Log"/> ولا يزعج المستخدم؛ التحديث ميزة كماليّة لا يجوز أن
///     تُسقط التطبيق أو تقاطع عمله.
///   • <b>لا أثر في التطوير</b> — حين لا يعمل التطبيق من تثبيت Velopack (تشغيل F5 أو
///     <c>dotnet run</c>) يكون <see cref="IsInstalled"/> كاذباً، فلا تُجرى أيّ نداءات شبكة
///     ويسلك التطبيق كما كان تماماً.
/// </summary>
public static class UpdateService
{
    /// <summary>مستودع الإصدارات — عامّ، فالوصول المجهول يكفي ولا نضمّن أيّ رمز وصول في العميل.</summary>
    private const string RepoUrl = "https://github.com/Ali7med/Terminal-engine";

    /// <summary>
    /// مدير التحديث — كسول: يُبنى مرّة واحدة عند أوّل استعمال. يعود <c>null</c> إن لم يكن
    /// التطبيق مثبَّتاً عبر Velopack (تطوير) أو إن فشل بناؤه — وهو مِصفاة الحماية الوحيدة:
    /// كلّ ما تحت يتحقّق منه أوّلاً، فلا شبكة في التطوير.
    /// </summary>
    private static readonly Lazy<UpdateManager?> Manager =
        new(CreateManager, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>الحزمة المنزَّلة المنتظِرة إعادة التشغيل في هذه الجلسة (إن وُجدت).</summary>
    private static VelopackAsset? _pending;

    /// <summary>مانع تداخل: يمنع فحصاً ثانياً بالتوازي مع فحص جارٍ (0 = خامل، 1 = يعمل).</summary>
    private static int _busy;

    /// <summary>يحرس ربط <see cref="Application.Exit"/> مرّة واحدة فقط.</summary>
    private static bool _exitHooked;

    private static UpdateManager? CreateManager()
    {
        try
        {
            // البنّاء لا يتّصل بالشبكة؛ الاتّصال يبدأ عند CheckForUpdatesAsync فقط.
            var manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
            // غير مثبَّت ⇒ لا مدير ⇒ كلّ الخدمة تصير لا-عمليّة (no-op) في التطوير.
            return manager.IsInstalled ? manager : null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log(ex, "UpdateService");
            return null;
        }
    }

    /// <summary>صحيح فقط حين يعمل التطبيق من تثبيت Velopack — كاذب في التطوير/التنقيح.</summary>
    public static bool IsInstalled => Manager.Value is not null;

    /// <summary>
    /// النسخة المنزَّلة المنتظِرة إعادة التشغيل، وإلّا <c>null</c>. تشمل تحديثاً نُزِّل في جلسة
    /// سابقة ولم يُطبَّق بعد (عبر <c>UpdatePendingRestart</c>)، لا تحديثَ هذه الجلسة وحده.
    /// </summary>
    public static string? PendingVersion
    {
        get
        {
            try
            {
                return (_pending ?? Manager.Value?.UpdatePendingRestart)?.Version?.ToString();
            }
            catch (Exception ex)
            {
                CrashReporter.Log(ex, "UpdateService");
                return null;
            }
        }
    }

    /// <summary>
    /// فحص صامت ثمّ تنزيل في الخلفيّة. آمنة للنداء دون انتظار (fire-and-forget) — لا ترمي أبداً.
    /// تعود فوراً إن لم يكن التطبيق مثبَّتاً أو إن لم يوجد تحديث.
    /// </summary>
    public static async Task CheckAndDownloadAsync()
    {
        // فحص جارٍ بالفعل ⇒ اخرج بلا ضجّة.
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;

        try
        {
            UpdateManager? manager = Manager.Value;
            if (manager is null) return;   // تطوير/غير مثبَّت ⇒ لا شبكة البتّة

            UpdateInfo? info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null) return;      // لا جديد

            await manager.DownloadUpdatesAsync(info).ConfigureAwait(false);

            _pending = info.TargetFullRelease;
            ArmApplyOnExit();

            string version = _pending?.Version?.ToString() ?? string.Empty;
            NotificationService.Info(
                Loc.T("update.ready"),
                // ثابت الثقافة: النصّ مترجَم أصلاً، والوسيط رقم نسخة لاتينيّ لا يُحلَّى محليّاً.
                string.Format(CultureInfo.InvariantCulture, Loc.T("update.readyMsg"), version));
        }
        catch (Exception ex)
        {
            // فشل الشبكة/التنزيل صامت تماماً: لا حوار ولا إشعار — سطر في السجلّ فقط.
            CrashReporter.Log(ex, "UpdateService");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// يربط تطبيق التحديث بلحظة خروج التطبيق. لا يجوز نداء <c>WaitExitThenApplyUpdates</c> فور
    /// انتهاء التنزيل: فهو يُطلق مُحدِّث Velopack لينتظر خروجَ هذه العمليّة، ومهلته 60 ثانية فقط
    /// — والمستخدم قد يُبقي التطبيق مفتوحاً ساعات. فنؤجّله إلى حدث <see cref="Application.Exit"/>
    /// حيث لا ينتظر المحدِّث إلّا لحظات.
    /// </summary>
    private static void ArmApplyOnExit()
    {
        try
        {
            Application? app = Application.Current;
            if (app is null) return;

            // الربط على خيط الواجهة (نحن على خيط مهمّة خلفيّة بعد ConfigureAwait(false)).
            app.Dispatcher.Invoke(() =>
            {
                if (_exitHooked) return;
                _exitHooked = true;
                app.Exit += OnAppExit;
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log(ex, "UpdateService");
        }
    }

    /// <summary>
    /// عند الخروج: أطلق المحدِّث ليطبّق الحزمة المنزَّلة بصمت بعد انتهاء هذه العمليّة.
    /// <c>silent: true</c> فلا نافذة تقدّم، و<c>restart: false</c> فلا يُعاد فتح التطبيق —
    /// المستخدم اختار الخروج، والتحديث يظهر عند تشغيله القادم.
    /// </summary>
    private static void OnAppExit(object sender, ExitEventArgs e)
    {
        try
        {
            UpdateManager? manager = Manager.Value;
            VelopackAsset? asset = _pending;
            if (manager is null || asset is null) return;

            manager.WaitExitThenApplyUpdates(asset, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            CrashReporter.Log(ex, "UpdateService");
        }
    }
}
