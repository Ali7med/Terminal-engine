using System;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Servers.Models;

namespace Terminal.Servers.Ssh;

/// <summary>
/// اتّصال SSH بخادم واحد: يفتح جلسة، ينفّذ أوامر عن بُعد، ويُغلق. تجريد فوق SSH.NET كي يبقى
/// المنطق الأعلى (الماسح/المراقب) قابلاً للاختبار عبر بديل وهميّ (fake) بلا شبكة.
/// </summary>
public interface ISshConnection : IDisposable
{
    /// <summary>هل الجلسة مفتوحة الآن؟</summary>
    bool IsConnected { get; }

    /// <summary>يفتح الجلسة (يرمي عند فشل المصادقة/الشبكة).</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>ينفّذ أمراً عن بُعد ويُعيد الخرج/الخطأ/رمز الخروج.</summary>
    Task<CommandResult> RunAsync(string command, CancellationToken ct = default);

    /// <summary>يُغلق الجلسة (بلا رمي).</summary>
    void Disconnect();
}
