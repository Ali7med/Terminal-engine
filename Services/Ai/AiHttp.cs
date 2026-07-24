using System;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace TerminalLauncher.Services.Ai;

/// <summary>
/// عميل HTTP مشترك لطبقة الـAI. مهلته الكلّيّة <b>لا نهائيّة عمداً</b>: مهلة
/// <see cref="HttpClient.Timeout"/> تنطبق على الطلب كاملاً فتقطع ردّاً يتدفّق دقيقتين وهو سليم.
/// المهلة الصحيحة للبثّ مهلتان منفصلتان يديرهما المزوّد: مهلة اتّصال أوّليّ، ومهلة خمول بين المقاطع.
/// </summary>
internal static class AiHttp
{
    /// <summary>مهلة الاتّصال الأوّليّ وترويسات الاستجابة.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>أقصى مدّة بلا أيّ بايتات أثناء البثّ قبل اعتبار الاتّصال ميتاً.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>مهلة النداءات القصيرة (قائمة النماذج / اختبار الاتّصال).</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    private static readonly Lazy<HttpClient> Instance = new(() =>
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = ConnectTimeout,
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    });

    /// <summary>العميل المشترك (آمن للاستعمال المتزامن).</summary>
    public static HttpClient Client => Instance.Value;
}
