using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TerminalLauncher.Services.Ai;

/// <summary>
/// جلسة دردشة واحدة: تاريخ الرسائل + بثّ الردّ الجاري. مملوكة للتبويب الذي أنشأها وتُلغى عند إغلاقه.
///
/// <para><b>تجميع الدلتات (العقد الحاكم):</b> نداء <c>Dispatcher</c> لكلّ رمز مبثوث يعني مئات
/// الاستدعاءات في الثانية وإعادة قياس وتخطيط للنصّ في كلّ مرّة — تجميد مؤكَّد للواجهة. فالمقاطع
/// تتراكم في عازل يملؤه خيط الشبكة، ويفرّغه مؤقّت واحد على خيط الواجهة كلّ
/// <see cref="FlushInterval"/>.</para>
/// </summary>
public sealed class AiChatSession : IDisposable
{
    /// <summary>فترة تفريغ العازل إلى الواجهة.</summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(80);

    private readonly List<AiMessage> _history = new();
    private readonly StringBuilder _incoming = new();
    private readonly object _bufferLock = new();
    private readonly DispatcherTimer _flushTimer;

    private CancellationTokenSource? _cts;
    private bool _disposed;

    // عدّادات الاستهلاك. تصل مقاطعُها من خيط الشبكة وتُقرأ من خيط الواجهة، فتُحدَّث تحت القفل.
    private int _lastPromptTokens;
    private int _lastCompletionTokens;
    private int _sessionTokens;

    /// <summary>ينشئ جلسة برسالة نظام اختياريّة (البادئة الثابتة).</summary>
    public AiChatSession(string? systemPrompt = null)
    {
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            _history.Add(AiMessage.System(systemPrompt!));

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FlushInterval };
        _flushTimer.Tick += (_, _) => Flush();
    }

    /// <summary>مقاطع الردّ الجاري (نصّ/كود) — يقرأها العرض.</summary>
    public MarkdownStreamSegmenter Reply { get; } = new();

    /// <summary>تاريخ المحادثة المرسَل مع كلّ طلب.</summary>
    public IReadOnlyList<AiMessage> History => _history;

    /// <summary>هل هناك ردّ قيد الاستقبال الآن؟ (يمنع إغلاق التبويب بلا تحذير.)</summary>
    public bool IsStreaming => _cts is not null;

    /// <summary>توكنز إدخال آخر ردّ (0 إن لم يُبلغ المزوّد عنها).</summary>
    public int LastPromptTokens { get { lock (_bufferLock) return _lastPromptTokens; } }

    /// <summary>توكنز إخراج آخر ردّ (0 إن لم يُبلغ المزوّد عنها).</summary>
    public int LastCompletionTokens { get { lock (_bufferLock) return _lastCompletionTokens; } }

    /// <summary>إجمالي توكنز الجلسة المتراكمة (إدخال + إخراج) عبر كلّ الردود المبلَّغ عنها.</summary>
    public int SessionTokens { get { lock (_bufferLock) return _sessionTokens; } }

    /// <summary>هل أبلغ المزوّد عن استهلاك آخر ردّ؟ (لا تفعل كلّ المنصّات.)</summary>
    public bool HasUsage => LastPromptTokens > 0 || LastCompletionTokens > 0;

    /// <summary>يُطلَق على خيط الواجهة عند وصول محتوى جديد.</summary>
    public event Action? Updated;

    /// <summary>يُطلَق عند اكتمال الردّ بنجاح.</summary>
    public event Action? Completed;

    /// <summary>يُطلَق عند الفشل برسالة جاهزة للعرض وإجراء مقترح.</summary>
    public event Action<AiErrorView>? Failed;

    /// <summary>
    /// يرسل رسالة مستخدم ويبدأ بثّ الردّ. طلب جديد أثناء بثّ جارٍ يلغي السابق.
    /// </summary>
    public void Send(IAiProvider provider, string userText, AiChatOptions options)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));
        if (string.IsNullOrWhiteSpace(userText)) return;

        Cancel();

        _history.Add(AiMessage.User(userText.Trim()));
        Reply.Reset();
        lock (_bufferLock)
        {
            _incoming.Clear();
            _lastPromptTokens = 0;
            _lastCompletionTokens = 0;
        }

        _cts = new CancellationTokenSource();
        _flushTimer.Start();

        // النسخة المُلتقَطة للتاريخ: الطلب الشبكيّ لا يقرأ قائمة قابلة للتعديل من خيط الواجهة.
        var snapshot = new List<AiMessage>(_history);
        CancellationToken token = _cts.Token;

        _ = StreamAsync(provider, snapshot, options, token);
    }

    private async Task StreamAsync(
        IAiProvider provider,
        IReadOnlyList<AiMessage> messages,
        AiChatOptions options,
        CancellationToken token)
    {
        try
        {
            await foreach (AiDelta delta in provider.ChatStreamAsync(messages, options, token).ConfigureAwait(false))
            {
                if (delta.IsUsage)
                {
                    lock (_bufferLock)
                    {
                        if (delta.PromptTokens is int p) _lastPromptTokens = p;
                        if (delta.CompletionTokens is int c) _lastCompletionTokens = c;
                    }
                    continue;
                }
                if (delta.Text.Length == 0) continue;
                lock (_bufferLock) _incoming.Append(delta.Text);
            }

            Finish(token, error: null);
        }
        catch (AiException ex)
        {
            Finish(token, ex);
        }
        catch (OperationCanceledException)
        {
            Finish(token, error: null);
        }
        catch (Exception ex)
        {
            Finish(token, new AiException(AiErrorKind.Provider, provider.DisplayName, options.Model, ex.Message, inner: ex));
        }
    }

    /// <summary>ينهي البثّ على خيط الواجهة: تفريغ أخير ثمّ حفظ الردّ في التاريخ ثمّ الحدث المناسب.</summary>
    private void Finish(CancellationToken token, AiException? error)
    {
        _flushTimer.Dispatcher.InvokeAsync(() =>
        {
            // بثّ قديم أُلغي بعد أن بدأ بثّ جديد: لا يلمس حالة الجلسة الحاليّة.
            if (_cts is null || _cts.Token != token) return;

            Flush();
            Reply.Complete();
            lock (_bufferLock) _sessionTokens += _lastPromptTokens + _lastCompletionTokens;
            _flushTimer.Stop();
            _cts.Dispose();
            _cts = null;

            string text = Reply.RawText();
            if (text.Length > 0) _history.Add(AiMessage.Assistant(text));

            Updated?.Invoke();

            if (error is null) Completed?.Invoke();
            else if (error.Kind != AiErrorKind.Canceled) Failed?.Invoke(AiErrorPresenter.Present(error));
        });
    }

    /// <summary>يفرّغ العازل إلى المقاطع ويُخطر العرض. يعمل على خيط الواجهة حصراً.</summary>
    private void Flush()
    {
        string chunk;
        lock (_bufferLock)
        {
            if (_incoming.Length == 0) return;
            chunk = _incoming.ToString();
            _incoming.Clear();
        }

        Reply.Append(chunk);
        Updated?.Invoke();
    }

    /// <summary>يلغي البثّ الجاري إن وُجد (إغلاق التبويب، أو زرّ «إيقاف»).</summary>
    public void Cancel()
    {
        CancellationTokenSource? cts = _cts;
        if (cts is null) return;

        _cts = null;
        _flushTimer.Stop();
        try { cts.Cancel(); } catch (ObjectDisposedException) { /* أُلغيت أصلاً */ }
        cts.Dispose();
    }

    /// <summary>يبدأ محادثة جديدة مع الإبقاء على رسالة النظام.</summary>
    public void Clear()
    {
        Cancel();
        for (int i = _history.Count - 1; i >= 0; i--)
            if (_history[i].Role != AiRole.System)
                _history.RemoveAt(i);

        Reply.Reset();
        lock (_bufferLock)
        {
            _incoming.Clear();
            _lastPromptTokens = 0;
            _lastCompletionTokens = 0;
            _sessionTokens = 0;
        }
        Updated?.Invoke();
    }

    /// <summary>نصّ المحادثة كاملاً — لزرّ «نسخ المحادثة» (الاسترجاع اليدويّ بديل الحفظ على القرص).</summary>
    public string Transcript()
    {
        var sb = new StringBuilder();
        foreach (AiMessage m in _history)
        {
            if (m.Role == AiRole.System) continue;
            sb.Append(m.Role == AiRole.User ? Loc.T("ai.panel.you") : Loc.T("ai.panel.title"))
              .Append(":\n")
              .Append(m.Content)
              .Append("\n\n");
        }
        return sb.ToString().TrimEnd();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cancel();
        _flushTimer.Stop();
    }
}
