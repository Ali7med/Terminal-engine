using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Ssh;

namespace Terminal.Servers.Scan;

/// <summary>
/// يسرد كلّ حاويات Docker على الخادم عبر <c>docker ps -a</c> بصيغة حقول مفصولة بـ '|' (لا يظهر في
/// المعرّف/الاسم/الحالة/الصورة). أمر السرد مبنيّ بشكل قابل للاختبار (<see cref="BuildList"/>) ويُحلَّل
/// عبر <see cref="Parse"/>. لا يرمي عند غياب Docker/الصلاحيّة — يُعيد قائمة فارغة.
/// </summary>
public sealed class DockerContainers
{
    // الترتيب: ID | Names | State | Status | Image. الحقول لا تحوي '|'؛ أأمن من \t في قوالب Go.
    private const string ListFormat = "{{.ID}}|{{.Names}}|{{.State}}|{{.Status}}|{{.Image}}";

    private readonly ISshConnection _ssh;
    private readonly bool _sudo;

    public DockerContainers(ISshConnection ssh, bool sudo = false)
    {
        _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));
        _sudo = sudo;
    }

    /// <summary>
    /// أمر سرد كلّ الحاويات (الشغّالة والمتوقّفة) بصيغة مفصولة بـ '|'. <paramref name="sudo"/> يسبق
    /// الأمر بـ <c>sudo -n</c> (غير تفاعليّ — يتطلّب NOPASSWD إذ لا tty في جلسة exec).
    /// </summary>
    public static string BuildList(bool sudo = false)
        => DockerCli.Prefix(sudo) + "docker ps -a --no-trunc --format '" + ListFormat + "' 2>/dev/null";

    /// <summary>يحلّل مخرجات <see cref="BuildList"/> إلى قائمة حاويات (يتخطّى الأسطر الناقصة).</summary>
    public static IReadOnlyList<ContainerListItem> Parse(string stdout)
    {
        var list = new List<ContainerListItem>();
        foreach (var raw in (stdout ?? string.Empty).Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            var f = line.Split('|');
            if (f.Length < 5) continue;

            // اسم متعدّد (نادر) يفصله docker بفاصلة — نأخذ الأوّل للعرض.
            string name = f[1].Trim();
            int comma = name.IndexOf(',');
            if (comma >= 0) name = name.Substring(0, comma);

            string id = f[0].Trim();
            if (id.Length == 0) continue;

            list.Add(new ContainerListItem(
                Id: id,
                Name: name,
                State: f[2].Trim(),
                Status: f[3].Trim(),
                Image: f[4].Trim()));
        }
        return list;
    }

    /// <summary>يسرد كلّ الحاويات على الخادم. لا يرمي — يُعيد قائمة فارغة إن تعذّر (Docker غائب/بلا صلاحية).</summary>
    public async Task<IReadOnlyList<ContainerListItem>> ListAsync(CancellationToken ct = default)
    {
        var r = await _ssh.RunAsync(BuildList(_sudo), ct).ConfigureAwait(false);
        return Parse(r.StdOut);
    }
}
