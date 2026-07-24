using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TerminalLauncher.Services.Ai;

/// <summary>
/// محوّل Anthropic (Messages API الأصليّ). هو المحوّل الثاني والأخير: طبقة توافق Anthropic مع
/// OpenAI محدودة، فالمسار الأصليّ أضمن — بينما بقيّة المنصّات كلّها تمرّ عبر
/// <see cref="OpenAiCompatProvider"/>.
/// <para>فروقه عن المسار المتوافق: المصادقة بترويسة <c>x-api-key</c> (لا Bearer)، وترويسة إصدار
/// إلزاميّة، ونصّ النظام حقل مستقلّ لا رسالة بدور، و<c>max_tokens</c> إلزاميّ.</para>
/// </summary>
public sealed class AnthropicProvider : IAiProvider
{
    /// <summary>إصدار الـAPI المُثبَّت — ترويسة إلزاميّة عند Anthropic.</summary>
    private const string ApiVersion = "2023-06-01";

    /// <summary>حدّ افتراضيّ للردّ حين لا يحدّده المستدعي (الحقل إلزاميّ عند هذا المزوّد).</summary>
    private const int DefaultMaxTokens = 4096;

    private readonly AiProviderDescriptor _descriptor;
    private readonly string _baseUrl;
    private readonly string? _apiKey;

    public AnthropicProvider(AiProviderDescriptor descriptor, string? apiKey, string? baseUrlOverride = null)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _baseUrl = (string.IsNullOrWhiteSpace(baseUrlOverride) ? descriptor.BaseUrl : baseUrlOverride!).TrimEnd('/');
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey!.Trim();
    }

    public string DisplayName => _descriptor.DisplayName;

    public async IAsyncEnumerable<AiDelta> ChatStreamAsync(
        IReadOnlyList<AiMessage> messages,
        AiChatOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_apiKey is null)
            throw Fail(AiErrorKind.Auth, options.Model, "لم يُدخَل مفتاح لهذا المزوّد.");

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/messages")
        {
            Content = new StringContent(BuildBody(messages, options), Encoding.UTF8, "application/json"),
        };
        ApplyHeaders(request);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(AiHttp.ConnectTimeout);

        HttpResponseMessage response;
        try
        {
            response = await AiHttp.Client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connectCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw Fail(AiErrorKind.Canceled, options.Model, "أُلغي الطلب.");
        }
        catch (OperationCanceledException)
        {
            throw Fail(AiErrorKind.Timeout, options.Model, "انتهت مهلة الاتّصال بالمزوّد.");
        }
        catch (HttpRequestException ex)
        {
            throw Fail(AiErrorKind.Network, options.Model, "تعذّر الوصول إلى المزوّد.", inner: ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            string detail = await SafeReadAsync(response, ct).ConfigureAwait(false);
            AiException error = AiErrorMapper.Classify(response, detail, _descriptor.DisplayName, options.Model);
            response.Dispose();
            throw error;
        }

        using (response)
        {
            System.IO.Stream body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            var events = SseReader.ReadAsync(
                body,
                AiHttp.IdleTimeout,
                onIdleTimeout: () => throw Fail(AiErrorKind.Timeout, options.Model,
                    $"انقطع البثّ: لا استجابة لمدّة {AiHttp.IdleTimeout.TotalSeconds:0} ثانية."),
                ct);

            await using var e = events.GetAsyncEnumerator(ct);
            while (true)
            {
                bool has;
                try
                {
                    has = await e.MoveNextAsync().ConfigureAwait(false);
                }
                catch (AiException) { throw; }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw Fail(AiErrorKind.Canceled, options.Model, "أُلغي الطلب.");
                }
                catch (Exception ex)
                {
                    throw Fail(AiErrorKind.Network, options.Model, "انقطع الاتّصال أثناء استقبال الردّ.", inner: ex);
                }

                if (!has) yield break;

                foreach (AiDelta delta in ParseEvent(e.Current, _descriptor.DisplayName, options.Model))
                    yield return delta;
            }
        }
    }

    /// <summary>
    /// يحلّل حدثاً واحداً. الأحداث التي تعنينا: <c>content_block_delta</c> (نصّ)،
    /// <c>message_start</c>/<c>message_delta</c> (استهلاك)، <c>error</c>. الباقي (ping، بداية/نهاية
    /// الكتل) يُتجاهَل بصمت.
    /// </summary>
    private static IEnumerable<AiDelta> ParseEvent(string payload, string providerName, string model)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(payload); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) yield break;

            string type = root.TryGetProperty("type", out JsonElement t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? ""
                : "";

            switch (type)
            {
                case "content_block_delta":
                    if (root.TryGetProperty("delta", out JsonElement d)
                        && d.ValueKind == JsonValueKind.Object
                        && d.TryGetProperty("text", out JsonElement txt)
                        && txt.ValueKind == JsonValueKind.String)
                    {
                        string? s = txt.GetString();
                        if (!string.IsNullOrEmpty(s)) yield return AiDelta.OfText(s!);
                    }
                    break;

                case "message_start":
                    if (root.TryGetProperty("message", out JsonElement msg)
                        && msg.TryGetProperty("usage", out JsonElement u0))
                    {
                        int? input = ReadInt(u0, "input_tokens");
                        if (input.HasValue) yield return AiDelta.OfUsage(input, null);
                    }
                    break;

                case "message_delta":
                    if (root.TryGetProperty("usage", out JsonElement u1))
                    {
                        int? output = ReadInt(u1, "output_tokens");
                        if (output.HasValue) yield return AiDelta.OfUsage(null, output);
                    }
                    break;

                case "error":
                    string message = root.TryGetProperty("error", out JsonElement err)
                                     && err.ValueKind == JsonValueKind.Object
                                     && err.TryGetProperty("message", out JsonElement m)
                        ? m.GetString() ?? "خطأ من المزوّد"
                        : "خطأ من المزوّد";
                    throw new AiException(AiErrorKind.Provider, providerName, model, message);
            }
        }
    }

    private static int? ReadInt(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object
           && obj.TryGetProperty(name, out JsonElement v)
           && v.ValueKind == JsonValueKind.Number
           && v.TryGetInt32(out int i)
            ? i
            : null;

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(AiHttp.ProbeTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/models");
        ApplyHeaders(request);

        HttpResponseMessage response;
        try
        {
            response = await AiHttp.Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw Fail(AiErrorKind.Timeout, "", "انتهت مهلة جلب قائمة النماذج.");
        }
        catch (HttpRequestException ex)
        {
            throw Fail(AiErrorKind.Network, "", "تعذّر الوصول إلى المزوّد.", inner: ex);
        }

        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw AiErrorMapper.Classify(response, text, _descriptor.DisplayName, model: "");

            var ids = new List<string>();
            try
            {
                using JsonDocument doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in data.EnumerateArray())
                        if (item.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String)
                            ids.Add(id.GetString()!);
                }
            }
            catch (JsonException)
            {
                // شكل غير متوقّع — القائمة اختياريّة، لا نُسقط النداء لأجلها.
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }
    }

    public async Task<AiProbeResult> TestConnectionAsync(CancellationToken ct)
    {
        if (_apiKey is null)
            return AiProbeResult.Failure(AiErrorKind.Auth, "لم يُدخَل مفتاح بعد.");

        try
        {
            IReadOnlyList<string> models = await ListModelsAsync(ct).ConfigureAwait(false);
            return AiProbeResult.Success(
                models.Count > 0 ? $"الاتّصال ناجح — {models.Count} نموذجاً متاحاً." : "الاتّصال ناجح.");
        }
        catch (AiException ex)
        {
            return AiProbeResult.Failure(ex.Kind, ex.Message);
        }
        catch (Exception ex)
        {
            return AiProbeResult.Failure(AiErrorKind.Provider, ex.Message);
        }
    }

    private string BuildBody(IReadOnlyList<AiMessage> messages, AiChatOptions options)
    {
        var buffer = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("model", options.Model);
            w.WriteBoolean("stream", true);
            w.WriteNumber("max_tokens", options.MaxTokens ?? DefaultMaxTokens);
            if (options.Temperature is double temp) w.WriteNumber("temperature", temp);

            // نصّ النظام حقل مستقلّ هنا (لا رسالة بدور system).
            string system = CollectSystem(messages);
            if (system.Length > 0) w.WriteString("system", system);

            w.WriteStartArray("messages");
            foreach ((AiRole role, string content) in Conversation(messages))
            {
                w.WriteStartObject();
                w.WriteString("role", role == AiRole.Assistant ? "assistant" : "user");
                w.WriteString("content", content);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string CollectSystem(IReadOnlyList<AiMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (AiMessage m in messages)
        {
            if (m.Role != AiRole.System) continue;
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(m.Content);
        }
        return sb.ToString();
    }

    /// <summary>
    /// رسائل المحادثة بلا رسائل النظام، مع دمج المتتالية من نفس الدور — هذا المزوّد يرفض
    /// رسالتين متتاليتين بنفس الدور.
    /// </summary>
    private static List<(AiRole Role, string Content)> Conversation(IReadOnlyList<AiMessage> messages)
    {
        var result = new List<(AiRole, string)>();
        foreach (AiMessage m in messages)
        {
            if (m.Role == AiRole.System) continue;
            if (result.Count > 0 && result[^1].Item1 == m.Role)
                result[^1] = (m.Role, result[^1].Item2 + "\n\n" + m.Content);
            else
                result.Add((m.Role, m.Content));
        }
        return result;
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        if (_apiKey is not null)
            request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return text.Length > 600 ? text[..600] : text;
        }
        catch
        {
            return string.Empty;
        }
    }

    private AiException Fail(AiErrorKind kind, string model, string message, Exception? inner = null)
        => new(kind, _descriptor.DisplayName, model, message, inner: inner);
}
