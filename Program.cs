using System;
using Velopack;

namespace TerminalLauncher;

/// <summary>
/// نقطة الدخول اليدويّة للتطبيق — تحلّ محلّ <c>Main</c> المولَّدة تلقائيّاً من <c>App.xaml</c>
/// (عبر <c>&lt;StartupObject&gt;</c> في ملفّ المشروع).
///
/// سببها الوحيد: <see cref="VelopackApp"/> يجب أن يعمل <b>قبل كلّ شيء آخر</b> — قبل بناء كائن
/// <see cref="App"/> نفسه، وقبل كتلة إعادة الإطلاق المنفصلة في <c>App.OnStartup</c>. فعند التثبيت
/// أو التحديث أو إزالة التثبيت يُطلق Velopack التطبيقَ بوسائط خطّافات خاصّة
/// (<c>--veloapp-install</c> ونظائرها)، فيلتقطها <see cref="VelopackApp.Run"/> وينفّذها ثمّ يُنهي
/// العمليّة فوراً. لو تأخّر هذا النداء لالتقطت كتلةُ إعادة الإطلاق تلك الوسائطَ أوّلاً وأعادت
/// الإطلاق بـ<c>--detached</c> وحدها — فتضيع وسائط الخطّاف ويفشل التثبيت/التحديث بصمت.
///
/// خارج التثبيت (تشغيل عاديّ أو تطوير) <see cref="VelopackApp.Run"/> لا يفعل شيئاً ويعود فوراً،
/// فيتابع الإقلاع كما كان تماماً.
/// </summary>
internal static class Program
{
    /// <summary>
    /// <c>STAThread</c> شرط لازم لـWPF. تُحاكي هذه الدالّة ما تولّده أدوات WPF حرفيّاً
    /// (<c>new App()</c> ثمّ <c>InitializeComponent()</c> ثمّ <c>Run()</c>) مع سبق نداء Velopack.
    /// </summary>
    [STAThread]
    public static void Main()
    {
        // أوّل سطر ينفَّذ في العمليّة كلّها — لا يجوز أن يسبقه شيء.
        VelopackApp.Build().Run();

        var app = new App();
        // يربط الموارد وStartupUri المعرَّفَين في App.xaml (مولَّدة في App.g.cs).
        app.InitializeComponent();
        app.Run();
    }
}
