using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TerminalLauncher.Services.Ai;

/// <summary>
/// محوّل كلّ المنصّات المتوافقة مع OpenAI: OpenAI، DeepSeek، Kimi (Moonshot)، Z.ai (GLM)،
/// xAI Grok، Mistral، OpenRouter، Gemini (عبر نقطته المتوافقة)، وOllama المحلّيّ.
/// <para>«التوافق مع OpenAI» طيف لا قيمة منطقيّة: الفروق العمليّة (الاستهلاك في البثّ، شكل أجسام
/// الأخطاء، اختياريّة المفتاح) تُحمل في <see cref="AiCapabilities"/> ويُعالَجها هذا المحوّل، ولا
/// تُفرَّع في المستدعين.</para>
/// </summary>
public sealed class OpenAiCompatProvider : IAiProvider
{
    private readonly AiProviderDescriptor _descriptor;
    private readonly string _baseUrl;
    private readonly string? _apiKey;

    /// <param name="descriptor">مدخلة الكتالوج (أو مدخلة مخصّصة).</param>
    /// <param name="apiKey">المفتاح المفكوك — null/فارغ مسموح فقط إن كان <see cref="AiCapabilities.KeyOptional"/>.</param>
    /// <param name="baseUrlOverride">عنوان أساس بديل من الإعدادات (يتجاوز عنوان الكتالوج).</param>
    public OpenAiCompatProvider(AiProviderDescriptor descriptor, string? apiKey, string? baseUrlOverride = null)
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
        HttpResponseMessage response = await SendChatAsync(messages, options, stream: true, ct).ConfigureAwait(false);

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

                foreach (AiDelta delta in ParseChunk(e.Current))
                    yield return delta;
            }
        }
    }

    /// <summary>
    /// يحلّل حمولة حدث واحد. متسامح عمداً: أيّ حمولة لا تُفهَم تُتجاهَل بدل إسقاط المحادثة —
    /// المنصّات تضيف حقولاً خاصّة بها (بصمات، أدوار وسيطة، محتوى تفكير) لا تعني العرض.
    /// </summary>
    private static IEnumerable<AiDelta> ParseChunk(string payload)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(payload); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) yield break;

            // خطأ وصل داخل تيّار مفتوح (بعض المنصّات تفعل ذلك بدل رمز حالة).
            if (root.TryGetProperty("error", out JsonElement err))
            {
                string message = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out JsonElement m)
                    ? m.GetString() ?? "خطأ من المزوّد"
                    : err.ToString();
                throw new AiException(AiErrorKind.Provider, "", "", message);
            }

            if (root.TryGetProperty("choices", out JsonElement choices)
                && choices.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement choice in choices.EnumerateArray())
                {
                    string? text = ExtractContent(choice);
                    if (!string.IsNullOrEmpty(text))
                        yield return AiDelta.OfText(text!);
                }
            }

            // الاستهلاك يصل عادةً في آخر حدث (ولا تُبلغ عنه كلّ المنصّات).
            if (root.TryGetProperty("usage", out JsonElement usage) && usage.ValueKind == JsonValueKind.Object)
            {
                int? prompt = ReadInt(usage, "prompt_tokens");
                int? completion = ReadInt(usage, "completion_tokens");
                if (prompt.HasValue || completion.HasValue)
                    yield return AiDelta.OfUsage(prompt, completion);
            }
        }
    }

    /// <summary>نصّ المقطع من <c>delta.content</c>، مع احتياط <c>message.content</c> لمن لا يبثّ.</summary>
    private static string? ExtractContent(JsonElement choice)
    {
        if (choice.ValueKind != JsonValueKind.Object) return null;

        if (choice.TryGetProperty("delta", out JsonElement delta)
            && delta.ValueKind == JsonValueKind.Object
            && delta.TryGetProperty("content", out JsonElement dc)
            && dc.ValueKind == JsonValueKind.String)
            return dc.GetString();

        if (choice.TryGetProperty("message", out JsonElement msg)
            && msg.ValueKind == JsonValueKind.Object
            && msg.TryGetProperty("content", out JsonElement mc)
            && mc.ValueKind == JsonValueKind.String)
            return mc.GetString();

        return null;
    }

    private static int? ReadInt(JsonElement obj, string name)
        => obj.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)
            ? i
            : null;

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(AiHttp.ProbeTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/models");
        ApplyAuth(request);

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
                throw Classify(response, text, model: "");

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
                // مزوّد لا يلتزم بشكل /models — ليس خطأً قاتلاً، القائمة اختياريّة أصلاً.
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }
    }

    public async Task<AiProbeResult> TestConnectionAsync(CancellationToken ct)
    {
        if (_apiKey is null && !_descriptor.Capabilities.KeyOptional)
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

    // ===== الإرسال =====

    private async Task<HttpResponseMessage> SendChatAsync(
        IReadOnlyList<AiMessage> messages,
        AiChatOptions options,
        bool stream,
        CancellationToken ct)
    {
        if (_apiKey is null && !_descriptor.Capabilities.KeyOptional)
            throw Fail(AiErrorKind.Auth, options.Model, "لم يُدخَل مفتاح لهذا المزوّد.");

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
        {
            Content = new StringContent(BuildBody(messages, options, stream), Encoding.UTF8, "application/json"),
        };
        ApplyAuth(request);

        // مهلة الاتّصال الأوّليّ فقط: تنتهي بمجرّد وصول الترويسات، فلا تقطع بثّاً طويلاً سليماً.
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
            AiException error = Classify(response, detail, options.Model);
            response.Dispose();
            throw error;
        }

        return response;
    }

    private string BuildBody(IReadOnlyList<AiMessage> messages, AiChatOptions options, bool stream)
    {
        var buffer = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("model", options.Model);
            w.WriteBoolean("stream", stream);

            if (options.MaxTokens is int max) w.WriteNumber("max_tokens", max);
            if (options.Temperature is double temp) w.WriteNumber("temperature", temp);

            // الاستهلاك ضمن البثّ غير مدعوم في كلّ المنصّات — نطلبه فقط لمن يعلنه.
            if (stream && _descriptor.Capabilities.UsageInStream)
            {
                w.WriteStartObject("stream_options");
                w.WriteBoolean("include_usage", true);
                w.WriteEndObject();
            }

            w.WriteStartArray("messages");
            WriteMessages(w, messages);
            w.WriteEndArray();

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// يكتب الرسائل. لمن لا يدعم دور «system» يُدمَج نصّ النظام في مقدّمة أوّل رسالة مستخدم
    /// بدل إسقاطه — إسقاط تعليمات النظام يغيّر سلوك الردّ صامتاً.
    /// </summary>
    private void WriteMessages(Utf8JsonWriter w, IReadOnlyList<AiMessage> messages)
    {
        bool supportsSystem = _descriptor.Capabilities.SupportsSystemRole;
        var pendingSystem = new StringBuilder();

        foreach (AiMessage m in messages)
        {
            if (m.Role == AiRole.System && !supportsSystem)
            {
                if (pendingSystem.Length > 0) pendingSystem.Append("\n\n");
                pendingSystem.Append(m.Content);
                continue;
            }

            string content = m.Content;
            if (pendingSystem.Length > 0 && m.Role == AiRole.User)
            {
                content = pendingSystem + "\n\n" + content;
                pendingSystem.Clear();
            }

            w.WriteStartObject();
            w.WriteString("role", RoleName(m.Role));
            w.WriteString("content", content);
            w.WriteEndObject();
        }

        // نصّ نظام بلا رسالة مستخدم بعده: يُرسَل رسالة مستخدم مستقلّة.
        if (pendingSystem.Length > 0)
        {
            w.WriteStartObject();
            w.WriteString("role", "user");
            w.WriteString("content", pendingSystem.ToString());
            w.WriteEndObject();
        }
    }

    private static string RoleName(AiRole role) => role switch
    {
        AiRole.System => "system",
        AiRole.Assistant => "assistant",
        _ => "user",
    };

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (_apiKey is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        // ترويسات تعريفيّة اختياريّة تطلبها بوّابة OpenRouter لنسب الاستعمال.
        if (string.Equals(_descriptor.Id, "openrouter", StringComparison.Ordinal))
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/HeliumRedTools/TerminalLauncher");
            request.Headers.TryAddWithoutValidation("X-Title", "TerminalLauncher");
        }
    }

    // ===== تطبيع الأخطاء =====

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

    private AiException Classify(HttpResponseMessage response, string body, string model)
        => AiErrorMapper.Classify(response, body, _descriptor.DisplayName, model);

    private AiException Fail(AiErrorKind kind, string model, string message, Exception? inner = null)
        => new(kind, _descriptor.DisplayName, model, message, inner: inner);
}
