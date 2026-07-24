using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TerminalLauncher.Services.Ai;

/// <summary>
/// فحص توفّر Ollama المحلّيّ — مسار «جرّب الآن بلا مفتاح» في بطاقة أوّل التشغيل.
/// <para><b>عند الطلب فقط:</b> يُنادى عند فتح الإعدادات أو عرض بطاقة أوّل التشغيل، لا استطلاعاً
/// دوريّاً في الخلفيّة. فحص متكرّر لخدمة قد لا تكون مثبَّتة أصلاً هو إهدار صامت لا يراه أحد.</para>
/// </summary>
public static class OllamaProbe
{
    /// <summary>مهلة قصيرة عمداً: الخدمة محلّيّة، فإمّا تردّ فوراً أو ليست شغّالة.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>هل تعمل خدمة Ollama على هذا الجهاز الآن؟ لا ترمي أبداً.</summary>
    public static async Task<bool> IsRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, AiProviderCatalog.OllamaProbeUrl);
            using HttpResponseMessage response = await AiHttp.Client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false; // غير مثبَّتة/غير شغّالة — ليست حالة خطأ تُعرض
        }
    }
}
