using System;
using System.IO;
using System.Text;
using Renci.SshNet;
using Terminal.Servers.Models;

namespace Terminal.Servers.Ssh;

/// <summary>
/// يبني <see cref="ConnectionInfo"/> من <see cref="SshConnectionInfo"/> (كلمة مرور أو مفتاح PEM).
/// مشترك بين اتّصال exec (<see cref="SshNetConnection"/>) واتّصال SFTP (<see cref="SshNetSftp"/>).
/// </summary>
internal static class ConnectionInfoFactory
{
    public static ConnectionInfo Build(SshConnectionInfo info, TimeSpan timeout)
    {
        ConnectionInfo ci;
        if (info.AuthKind == SshAuthKind.PrivateKey)
        {
            if (string.IsNullOrWhiteSpace(info.PrivateKeyPem))
                throw new InvalidOperationException("المفتاح الخاصّ فارغ.");

            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(info.PrivateKeyPem));
            var keyFile = string.IsNullOrEmpty(info.PrivateKeyPassphrase)
                ? new PrivateKeyFile(keyStream)
                : new PrivateKeyFile(keyStream, info.PrivateKeyPassphrase);
            ci = new PrivateKeyConnectionInfo(info.Host, info.Port, info.Username, keyFile);
        }
        else
        {
            ci = new PasswordConnectionInfo(info.Host, info.Port, info.Username, info.Password ?? string.Empty);
        }

        ci.Timeout = timeout;
        return ci;
    }
}
