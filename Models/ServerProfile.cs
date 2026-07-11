using System;
using Terminal.Servers.Models;

namespace TerminalLauncher.Models;

/// <summary>
/// بروفايل خادم كما تراه الواجهة (قابل للتحرير والربط). الأسرار الخام (<see cref="Secret"/>/
/// <see cref="KeyPassphrase"/>) تبقى null عند التحميل — لا تُفكّ إلّا عند الاتّصال — وتُحمَل النسخة
/// المُعمّاة في <see cref="SecretCipher"/>/<see cref="KeyPassphraseCipher"/>. عند التحرير: ملء الحقل
/// السرّيّ يعيد التعمية؛ تركه فارغاً يُبقي السرّ المخزَّن كما هو.
/// </summary>
public sealed class ServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public SshAuthKind AuthKind { get; set; } = SshAuthKind.Password;

    /// <summary>سرّ خام (كلمة مرور أو محتوى مفتاح PEM) — يُملأ فقط عند إدخال المستخدم؛ null بعد التحميل.</summary>
    public string? Secret { get; set; }

    /// <summary>عبارة مرور المفتاح الخاصّ الخام — تُملأ فقط عند الإدخال.</summary>
    public string? KeyPassphrase { get; set; }

    /// <summary>السرّ المُعمّى المخزَّن (base64 DPAPI).</summary>
    public string? SecretCipher { get; set; }

    /// <summary>عبارة مرور المفتاح المُعمّاة المخزَّنة.</summary>
    public string? KeyPassphraseCipher { get; set; }

    public string? Color { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? LastConnected { get; set; }
    public int SortOrder { get; set; }

    /// <summary>سطر عرض مختصر «user@host:port».</summary>
    public string DisplaySubtitle => $"{Username}@{Host}:{Port}";

    /// <summary>هل يملك سرّاً مخزَّناً (لتلميح حقل التحرير)؟</summary>
    public bool HasStoredSecret => !string.IsNullOrEmpty(SecretCipher);

    /// <summary>لون البطاقة الفعليّ (افتراضيّ إن غاب).</summary>
    public string EffectiveColor => string.IsNullOrWhiteSpace(Color) ? "#3B82F6" : Color!;

    /// <summary>الحرف الأوّل من الاسم (لدائرة الخادم في الشريط المطويّ).</summary>
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim().Substring(0, 1).ToUpperInvariant();

    public ServerProfile Clone() => (ServerProfile)MemberwiseClone();
}
