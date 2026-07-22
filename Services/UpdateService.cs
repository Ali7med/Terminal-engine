using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TerminalLauncher.Views;
using Velopack;
using Velopack.Sources;

namespace TerminalLauncher.Services;

/// <summary>
/// التحديث من إصدارات GitHub العامّة للمستودع عبر Velopack.
///
/// السياسة المعتمَدة — <b>التحديث بموافقة المستخدم</b> في أربع مراحل مفصولة:
///   1. <see cref="CheckAsync"/> — فحص فقط (صامت عند الإقلاع، ناطق عند الطلب اليدويّ). لا يُنزّل شيئاً.
///   2. إشعار قابل للنقر + شارة على شريط العنوان عبر حدث <see cref="UpdateAvailable"/>.
///   3. <see cref="DownloadAndApplyAsync"/> — حوار موافقة ← تنزيل بشريط تقدّم حيّ ← حوار إعادة تشغيل.
///   4. التطبيق: فوراً عبر <c>ApplyUpdatesAndRestart</c>، أو مؤجَّلاً إلى الخروج عبر <see cref="ArmApplyOnExit"/>.
/// لا يُنزَّل شيء ولا يُطبَّق شيء دون ضغطة صريحة من المستخدم.
///
/// قاعدتان حاكمتان:
///   • <b>لا ترمي أبداً</b> — كلّ مسار شبكيّ مغلَّف، وأيّ فشل يُسجَّل عبر <see cref="CrashReporter.Log"/>؛
///     ويُعرَض للمستخدم فقط حين يكون هو من طلب العمليّة. التحديث ميزة كماليّة لا يجوز أن تُسقط التطبيق.
///   • <b>لا أثر في التطوير</b> — حين لا يعمل التطبيق من تثبيت Velopack (تشغيل F5 أو <c>dotnet run</c>)
///     يكون <see cref="IsInstalled"/> كاذباً، فلا تُجرى أيّ نداءات شبكة ويسلك التطبيق كما كان تماماً.
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

    /// <summary>نتيجة آخر فحص وجد جديداً ولم يُنزَّل بعد.</summary>
    private static UpdateInfo? _available;

    /// <summary>الحزمة المنزَّلة المنتظِرة التطبيق في هذه الجلسة (إن وُجدت).</summary>
    private static VelopackAsset? _pending;

    /// <summary>مانع تداخل يغطّي الفحص والتنزيل معاً (0 = خامل، 1 = يعمل).</summary>
    private static int _busy;

    /// <summary>يحرس ربط <see cref="Application.Exit"/> مرّة واحدة فقط.</summary>
    private static bool _exitHooked;

    /// <summary>
    /// يُطلَق عند اكتشاف نسخة أحدث (الوسيط = رقم النسخة الجديدة). تشترك به الواجهة لإظهار شارة
    /// دائمة على شريط العنوان. يُطلَق دوماً على خيط الواجهة.
    /// </summary>
    public static event Action<string>? UpdateAvailable;

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

    /// <summary>رقم النسخة الأحدث المكتشَفة والتي لم تُنزَّل بعد، وإلّا <c>null</c>.</summary>
    public static string? AvailableVersion => _available?.TargetFullRelease?.Version?.ToString();

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
    /// فحص إصدارات GitHub — <b>لا يُنزّل شيئاً</b>. آمنة للنداء دون انتظار (fire-and-forget)، لا ترمي أبداً.
    /// عند <paramref name="silent"/> = <c>false</c> (طلب يدويّ) تُظهر نتيجةً في كلّ الحالات:
    /// «تحديث متوفّر» أو «أنت على أحدث نسخة» أو «تعذّر التحديث». وعند <c>true</c> (الإقلاع) لا تُظهر
    /// شيئاً إلّا حين يوجد جديد فعلاً.
    /// </summary>
    public static async Task CheckAsync(bool silent)
    {
        UpdateManager? manager = Manager.Value;
        if (manager is null) return;   // تطوير/غير مثبَّت ⇒ لا شبكة البتّة

        // نُزِّل تحديث بالفعل في هذه الجلسة ⇒ لا فائدة من فحص جديد، اعرض ما بين يديك.
        if (_pending is not null)
        {
            if (!silent) OfferPending();
            return;
        }

        // فحص أو تنزيل جارٍ بالفعل ⇒ اخرج بلا ضجّة.
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;

        try
        {
            UpdateInfo? info = await manager.CheckForUpdatesAsync().ConfigureAwait(true);
            _available = info;

            if (info is null)
            {
                if (!silent)
                    NotificationService.Success(
                        Loc.T("update.upToDate"),
                        Format(Loc.T("update.upToDateMsg"), AppVersion.Current));
                return;
            }

            string version = info.TargetFullRelease?.Version?.ToString() ?? "";
            UpdateAvailable?.Invoke(version);
            NotificationService.Primary(
                Loc.T("update.available"),
                Format(Loc.T("update.availableMsg"), version, AppVersion.Current),
                NotificationType.Info,
                seconds: 12,
                onClick: () => _ = DownloadAndApplyAsync(Application.Current?.MainWindow));
        }
        catch (Exception ex)
        {
            // الفشل الصامت يبقى صامتاً؛ أمّا الطلب اليدويّ فيستحقّ جواباً.
            CrashReporter.Log(ex, "UpdateService");
            if (!silent) NotificationService.Error(Loc.T("update.failed"), Loc.T("update.failedMsg"));
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// التدفّق الكامل بموافقة المستخدم: حوار تأكيد ← تنزيل بشريط تقدّم ← حوار إعادة التشغيل.
    /// تُنادى من نقرة الإشعار أو من بند «تحقّق من التحديثات». لا ترمي أبداً.
    /// يجب نداؤها من خيط الواجهة (تفتح حوارات نمطيّة).
    /// </summary>
    public static async Task DownloadAndApplyAsync(Window? owner)
    {
        UpdateManager? manager = Manager.Value;
        if (manager is null) return;

        // نُزِّل سلفاً (ضغط «لاحقاً» قبل قليل مثلاً) ⇒ تخطَّ التنزيل إلى سؤال إعادة التشغيل.
        if (_pending is not null) { AskRestart(owner, manager); return; }

        UpdateInfo? info = _available;
        if (info is null)
        {
            await CheckAsync(silent: false).ConfigureAwait(true);
            info = _available;
            if (info is null) return;   // لا جديد (أو فشل) — والفحص أخبر المستخدم أصلاً
        }

        string version = info.TargetFullRelease?.Version?.ToString() ?? "";

        string? answer = AppDialog.Confirm(owner,
            Format(Loc.T("update.confirmTitle"), version),
            Loc.T("update.confirmMsg"),
            (Loc.T("update.later"), "later", DialogButtonKind.Neutral),
            (Loc.T("update.now"), "go", DialogButtonKind.Accent));
        if (answer != "go") return;

        if (Interlocked.Exchange(ref _busy, 1) == 1) return;

        var cts = new CancellationTokenSource();
        ProgressNotification progress = NotificationService.Progress(
            Loc.T("update.downloading"), "0%", onCancel: cts.Cancel);

        try
        {
            // Velopack يبلّغ نسبة مئويّة صحيحة (0..100)، وReport يُوجَّه إلى خيط الواجهة داخلياً.
            await manager.DownloadUpdatesAsync(
                info,
                pct => progress.Report(pct / 100.0, Format("{0}%", pct)),
                cts.Token).ConfigureAwait(true);

            _pending = info.TargetFullRelease;
            _available = null;
            ArmApplyOnExit();

            progress.Done(Loc.T("update.ready"), Format(Loc.T("update.readyMsg"), version));
            AskRestart(owner, manager);
        }
        catch (OperationCanceledException)
        {
            // المستخدم ألغى بنفسه — البطاقة أُزيلت عند ضغط X، فلا رسالة إضافيّة.
        }
        catch (Exception ex)
        {
            CrashReporter.Log(ex, "UpdateService");
            progress.Fail(Loc.T("update.failed"), Loc.T("update.failedMsg"));
        }
        finally
        {
            cts.Dispose();
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// سؤال إعادة التشغيل بعد اكتمال التنزيل. «الآن» يُغلق العمليّة ويُعيد إطلاق النسخة الجديدة فوراً؛
    /// و«لاحقاً» يترك الحزمة مسلَّحة على الخروج (<see cref="OnAppExit"/>) فتُطبَّق بصمت عند الإغلاق.
    /// </summary>
    private static void AskRestart(Window? owner, UpdateManager manager)
    {
        try
        {
            VelopackAsset? asset = _pending;
            if (asset is null) return;
            string version = asset.Version?.ToString() ?? "";

            string? answer = AppDialog.Confirm(owner,
                Format(Loc.T("update.restartTitle"), version),
                Loc.T("update.restartMsg"),
                (Loc.T("update.later"), "later", DialogButtonKind.Neutral),
                (Loc.T("update.restartNow"), "restart", DialogButtonKind.Accent));

            if (answer != "restart") return;

            // لا يعود من هنا: يُنهي العمليّة الحاليّة ويُطلق النسخة الجديدة.
            manager.ApplyUpdatesAndRestart(asset);
        }
        catch (Exception ex)
        {
            CrashReporter.Log(ex, "UpdateService");
        }
    }

    /// <summary>عرض حزمة منزَّلة تنتظر إعادة التشغيل (نتيجة فحص يدويّ بعد تنزيل سابق).</summary>
    private static void OfferPending()
        => NotificationService.Primary(
            Loc.T("update.ready"),
            Format(Loc.T("update.readyMsg"), PendingVersion ?? ""),
            NotificationType.Success,
            seconds: 12,
            onClick: () => _ = DownloadAndApplyAsync(Application.Current?.MainWindow));

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

    /// <summary>ثابت الثقافة: النصّ مترجَم أصلاً، ووسائطه أرقام نسخ لاتينيّة لا تُحلَّى محلّياً.</summary>
    private static string Format(string template, params object[] args)
        => string.Format(CultureInfo.InvariantCulture, template, args);
}
