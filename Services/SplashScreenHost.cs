using System;
using System.Threading;
using System.Windows.Threading;
using TerminalLauncher.Views;

namespace TerminalLauncher.Services;

/// <summary>
/// مضيف شاشة البدء: يُظهرها فور بدء الإقلاع ويغلقها حين تُرسَم النافذة الرئيسة أوّل مرّة.
///
/// <para><b>لماذا خيط مستقلّ؟</b> بناء <c>MainWindow</c> يحجز خيط الواجهة تماماً — تحليل XAML كبير،
/// فتح قاعدة البيانات، بناء بطاقات الثيمات والمعارض، ثمّ استعادة الجلسة (تشغيل صدفات فعليّة).
/// لو عُرضت الشاشة على الخيط نفسه لتجمّدت عند أوّل إطار: لا حلقة تدور ولا حتّى إعادة رسم إن غطّتها
/// نافذة أخرى. لذا تعمل على خيط <c>STA</c> بموزّع (Dispatcher) خاصّ به، فتبقى حيّةً متحرّكةً مهما
/// انشغل الخيط الرئيس.</para>
///
/// <para><b>حدود صارمة:</b> لا تُمرَّر عبر هذا الحدّ أيّ كائنات واجهة يملكها الخيط الرئيس (فراشٍ،
/// موارد التطبيق، عناصر) — التواصل نصوصٌ فقط. الشاشة نفسها مكتفية بذاتها (راجع
/// <see cref="SplashWindow"/>).</para>
///
/// <para>شاشة البدء ترف: أيّ فشل فيها يُبتلع ولا يُعطّل الإقلاع.</para>
/// </summary>
public static class SplashScreenHost
{
    private static readonly object Gate = new();

    private static SplashWindow? _window;
    private static bool _started;
    private static bool _closed;
    private static string? _pendingStatus;   // حالة وصلت قبل أن تُبنى النافذة

    /// <summary>
    /// يُطلق خيط الشاشة ويعود فوراً (لا ينتظر ظهورها) كي لا يؤخّر بناء النافذة الرئيسة.
    /// يُستدعى مرّة واحدة في <c>App.OnStartup</c> بعد كتلة إعادة الإطلاق المنفصلة.
    /// </summary>
    public static void Show()
    {
        lock (Gate)
        {
            if (_started || _closed) return;
            _started = true;
        }

        // تلميحات الإقلاع تُقرأ هنا (على الخيط الرئيس) قبل إطلاق الخيط: ملفّ نصّيّ صغير لا قاعدة
        // بيانات. وضبط اللغة مبكّراً يجعل نصوص الشاشة بلغة المستخدم — وتؤكّدها MainWindow لاحقاً
        // من الإعدادات الأصل.
        BootProfile.Hints hints;
        try
        {
            hints = BootProfile.Load();
            Loc.InitFromCode(hints.Lang);
        }
        catch { return; }

        var thread = new Thread(() => Run(hints))
        {
            IsBackground = true,   // لا يمنع خروج العمليّة مهما حدث
            Name = "SplashScreen",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>جسم خيط الشاشة: يبني النافذة ويُظهرها ثمّ يدير موزّعه حتّى يُنهيه <see cref="Close"/>.</summary>
    private static void Run(BootProfile.Hints hints)
    {
        SplashWindow window;
        try { window = new SplashWindow(hints); }
        catch { return; }

        lock (Gate)
        {
            if (_closed) return;   // انتهى الإقلاع قبل أن تُبنى — لا تُظهرها أصلاً
            _window = window;
            if (_pendingStatus != null) window.SetStatus(_pendingStatus);
        }

        try
        {
            window.Show();
            Dispatcher.Run();
        }
        catch { /* لا يجوز أن يُسقط خيطُ زينةٍ العمليّةَ */ }
    }

    /// <summary>
    /// يبدّل سطر المرحلة الجارية («استعادة الجلسة…» مثلاً). آمن من أيّ خيط، ويُحفَظ آخر نصّ إن
    /// وصل قبل أن تُبنى النافذة.
    /// </summary>
    public static void SetStatus(string text)
    {
        SplashWindow? window;
        lock (Gate)
        {
            if (_closed) return;
            _pendingStatus = text;
            window = _window;
        }
        if (window == null) return;

        try { window.Dispatcher.BeginInvoke(new Action(() => window.SetStatus(text))); }
        catch { /* أُغلقت بين اللحظتين */ }
    }

    /// <summary>
    /// يُغلق الشاشة بتلاشٍ قصير ثمّ يُنهي موزّع خيطها. يُستدعى عند <c>ContentRendered</c> للنافذة
    /// الرئيسة (أي بعد أوّل إطار مرسوم فعلاً)، وكذلك من مُبلِّغ الأعطال قبل عرض حوار خطأ كي لا
    /// تحجبه شاشةٌ Topmost. النداء المتكرّر بلا أثر.
    /// </summary>
    public static void Close()
    {
        SplashWindow? window;
        lock (Gate)
        {
            if (_closed) return;
            _closed = true;
            window = _window;
            _window = null;
        }
        if (window == null) return;

        try
        {
            window.Dispatcher.BeginInvoke(new Action(
                () => window.FadeOutAndClose(() => Dispatcher.CurrentDispatcher.InvokeShutdown())));
        }
        catch { /* الخيط انتهى سلفاً */ }
    }
}
