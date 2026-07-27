using System;
using System.Diagnostics;
using System.IO;

namespace TerminalLauncher.Services;

/// <summary>
/// يفتح دليل الاستعمال (صفحات HTML بجانب التطبيق) في متصفّح النظام، على القسم المطلوب.
///
/// <para><b>لماذا HTML في المتصفّح لا نافذة داخل التطبيق:</b> الدليل نصٌّ طويل بصور ووصلات
/// داخليّة — وهذا ما يجيده المتصفّح أصلاً: بحث بـCtrl+F، وتكبير، وطباعة، وفتح في نافذة ثانية
/// بجانب التطبيق أثناء اتّباع الخطوات. نافذة WebView داخل التطبيق كانت ستضيف اعتماديّة ثقيلة
/// مقابل تجربة أضعف.</para>
///
/// <para>اللغة تتبع لغة الواجهة: <c>guide.ar.html</c> أو <c>guide.en.html</c>. غياب ملفّ اللغة
/// يسقط إلى الآخر بدل أن يفتح لا شيء.</para>
/// </summary>
public static class DocsService
{
    /// <summary>مجلد الدليل بجانب ملفّ التطبيق التنفيذيّ.</summary>
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "docs");

    /// <summary>
    /// يفتح الدليل على مرساه. <paramref name="anchor"/> مثل <c>aliases</c> أو <c>shortcuts</c> —
    /// فارغ = أوّل الصفحة.
    /// </summary>
    public static void Open(string anchor = "")
    {
        string? path = Resolve();
        if (path is null)
        {
            NotificationService.Warning(Loc.T("docs.open"), Loc.T("docs.missing"));
            return;
        }

        try
        {
            // ‎file:// URI لا مسار عاديّ: المرسى (‎#anchor‎) جزءٌ من الـURI، ويُهمَل تماماً إن
            // سُلِّم المسار كما هو إلى الصدفة — فتُفتح الصفحة من أوّلها لا عند القسم المقصود.
            //
            // والمرسى يُلحَق بالنصّ لا عبر UriBuilder: مجلد التطوير هنا اسمه ‎C#‎ فيُرمَّز ‎%23‎
            // داخل الـURI، وUriBuilder معروفٌ بفكّ ترميز المسار ثمّ إعادة تركيبه — فيصير ذلك
            // الـ‎#‎ فاصلَ مرسى ويُبتَر المسار عنده.
            string url = new Uri(path).AbsoluteUri;
            if (anchor.Length > 0) url += "#" + anchor;

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            NotificationService.Warning(Loc.T("docs.open"), string.Format(Loc.T("docs.failed"), ex.Message));
        }
    }

    /// <summary>ملفّ لغة الواجهة، أو ملفّ اللغة الأخرى إن غاب، أو null إن غاب الاثنان.</summary>
    private static string? Resolve()
    {
        string preferred = Path.Combine(Folder, $"guide.{Loc.Code}.html");
        if (File.Exists(preferred)) return preferred;

        string fallback = Path.Combine(Folder, Loc.Current == AppLang.Ar ? "guide.en.html" : "guide.ar.html");
        return File.Exists(fallback) ? fallback : null;
    }
}
