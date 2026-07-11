using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Servers.Models;
using Terminal.Storage;
using TerminalLauncher.Models;

namespace TerminalLauncher.Services;

/// <summary>
/// يربط بروفايلات الخوادم بين طبقة التخزين (<see cref="ServerProfileStore"/>، أسرار مُعمّاة) ونماذج
/// الواجهة (<see cref="ServerProfile"/>). يتولّى التعمية/الفكّ (DPAPI) وبناء بيانات اتّصال SSH.
/// </summary>
public sealed class ServerProfileService
{
    private readonly ServerProfileStore _store;

    public ServerProfileService(ServerProfileStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>يحمّل كلّ البروفايلات (بلا فكّ الأسرار — تبقى مُعمّاة حتى الاتّصال).</summary>
    public List<ServerProfile> LoadAll()
        => _store.GetAll().Select(FromRow).ToList();

    /// <summary>
    /// يحفظ بروفايلاً (upsert). إن أُدخل سرّ خام جديد (<see cref="ServerProfile.Secret"/>) يُعمّى ويحلّ
    /// محلّ المخزَّن؛ وإلّا يُبقى السرّ المُعمّى الحاليّ. يمسح الحقول الخام بعد الحفظ.
    /// </summary>
    public void Save(ServerProfile p)
    {
        ArgumentNullException.ThrowIfNull(p);

        if (p.Secret is not null)
            p.SecretCipher = SecretProtector.Encrypt(p.Secret);
        if (p.KeyPassphrase is not null)
            p.KeyPassphraseCipher = SecretProtector.Encrypt(p.KeyPassphrase);

        _store.Upsert(ToRow(p));

        p.Secret = null;          // لا نُبقي الخام في الذاكرة بعد الحفظ
        p.KeyPassphrase = null;
    }

    public void Delete(string id) => _store.Delete(id);

    /// <summary>يختم «آخر اتّصال» بالوقت الحاليّ (UTC) ويحدّث البروفايل في الذاكرة.</summary>
    public void MarkConnected(ServerProfile p)
    {
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _store.UpdateLastConnected(p.Id, ts);
        p.LastConnected = DateTimeOffset.FromUnixTimeMilliseconds(ts);
    }

    /// <summary>
    /// يبني بيانات اتّصال SSH بفكّ السرّ المخزَّن (أو استعمال السرّ الخام إن كان محضّراً في الذاكرة).
    /// يرمي إن تعذّر فكّ السرّ (حساب/جهاز مختلف).
    /// </summary>
    public SshConnectionInfo BuildConnectionInfo(ServerProfile p)
    {
        ArgumentNullException.ThrowIfNull(p);
        string? secret = p.Secret ?? SecretProtector.Decrypt(p.SecretCipher);
        string? passphrase = p.KeyPassphrase ?? SecretProtector.Decrypt(p.KeyPassphraseCipher);

        return p.AuthKind == SshAuthKind.PrivateKey
            ? new SshConnectionInfo(p.Host, p.Port, p.Username, SshAuthKind.PrivateKey,
                PrivateKeyPem: secret, PrivateKeyPassphrase: passphrase)
            : new SshConnectionInfo(p.Host, p.Port, p.Username, SshAuthKind.Password, Password: secret);
    }

    private static ServerProfile FromRow(ServerProfileRow r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Host = r.Host,
        Port = r.Port,
        Username = r.Username,
        AuthKind = (SshAuthKind)r.AuthKind,
        SecretCipher = r.SecretCipher,
        KeyPassphraseCipher = r.KeyPassphraseCipher,
        Color = r.Color,
        Notes = r.Notes,
        LastConnected = r.LastConnectedUnixMs is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null,
        SortOrder = r.SortOrder,
    };

    private static ServerProfileRow ToRow(ServerProfile p) => new(
        p.Id, p.Name, p.Host, p.Port, p.Username, (int)p.AuthKind,
        p.SecretCipher, p.KeyPassphraseCipher, p.Color, p.Notes,
        p.LastConnected?.ToUnixTimeMilliseconds(), p.SortOrder);
}
