using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Servers.Models;
using Terminal.Servers.Ssh;

namespace Terminal.Servers.Scan;

/// <summary>
/// يربط مسار ملفّ على الخادم بحاوية Docker المالكة له. يدعم موقعَين شائعَين:
///   • طبقة overlay2:  <c>/var/lib/docker/overlay2/&lt;id&gt;/diff|merged/…</c> (ملفّات الحاوية الحيّة)
///   • حجم مُسمّى:      <c>/var/lib/docker/volumes/&lt;name&gt;/_data/…</c>
/// يستخرج المعرّف من المسار ثمّ يبحث في <c>docker inspect</c> لكلّ الحاويات عن تطابق. أوامر البحث
/// مبنيّة بشكل قابل للاختبار (<see cref="BuildOverlayLookup"/>/<see cref="BuildVolumeLookup"/>) وتُحلَّل عبر <see cref="Parse"/>.
///
/// ملاحظة: نطابق طبقة الكتابة فقط (UpperDir/MergedDir) وهي معرّف حصريّ لحاوية واحدة — فملفّ يُكتب وقت
/// التشغيل (سجلّ/كاش) يُربَط بمالكه بدقّة. الملفّات المخبوزة في صورة أساس مشتركة لن تُطابَق (طبقة سُفلى مشتركة).
/// </summary>
public sealed class DockerInspector
{
    private const string OverlayMarker = "/docker/overlay2/";
    private const string VolumeMarker = "/docker/volumes/";

    // حقول docker inspect مفصولة بـ '|' (لا يظهر في المسارات/الأسماء/الصور) — أأمن من \t في قوالب Go.
    // الترتيب: Name | Status | Image | coolify.name | compose.project | UpperDir | MergedDir
    private const string InspectFormat =
        "{{.Name}}|{{.State.Status}}|{{.Config.Image}}|" +
        "{{index .Config.Labels \"coolify.name\"}}|{{index .Config.Labels \"com.docker.compose.project\"}}|" +
        "{{.GraphDriver.Data.UpperDir}}|{{.GraphDriver.Data.MergedDir}}";

    // للحجوم: نستبدل حقلَي الطبقة بأسماء الأحجام المُركَّبة.
    private const string VolumeFormat =
        "{{.Name}}|{{.State.Status}}|{{.Config.Image}}|" +
        "{{index .Config.Labels \"coolify.name\"}}|{{index .Config.Labels \"com.docker.compose.project\"}}|" +
        "{{range .Mounts}}{{.Name}} {{end}}";

    private readonly ISshConnection _ssh;
    private readonly bool _sudo;

    public DockerInspector(ISshConnection ssh, bool sudo = false)
    {
        _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));
        _sudo = sudo;
    }

    /// <summary>يصنّف المسار ويستخرج معرّف الطبقة/الحجم (<see cref="DockerPathKind.None"/> إن كان خارج Docker).</summary>
    public static (DockerPathKind Kind, string Key) Classify(string path)
    {
        string p = path ?? string.Empty;
        if (Segment(p, OverlayMarker) is { } layer) return (DockerPathKind.Overlay, layer);
        if (Segment(p, VolumeMarker) is { } vol) return (DockerPathKind.Volume, vol);
        return (DockerPathKind.None, string.Empty);
    }

    /// <summary>المقطع الأوّل بعد علامة (مثلاً معرّف الطبقة بعد <c>overlay2/</c>). null إن غابت العلامة أو كان فارغاً.</summary>
    private static string? Segment(string path, string marker)
    {
        int i = path.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        int start = i + marker.Length;
        int end = path.IndexOf('/', start);
        string seg = end < 0 ? path[start..] : path[start..end];
        return seg.Length == 0 ? null : seg;
    }

    /// <summary>أمر البحث عن الحاوية المالكة لطبقة overlay2 (يفحص طبقة الكتابة لكلّ الحاويات ويصفّي بالمعرّف).</summary>
    public static string BuildOverlayLookup(string overlayId, bool sudo = false)
    {
        string d = DockerCli.Prefix(sudo);
        return d + "docker ps -aq 2>/dev/null | xargs -r " + d + "docker inspect --format '" + InspectFormat + "' 2>/dev/null " +
           "| grep -F -- " + StorageScanner.ShellQuote(overlayId);
    }

    /// <summary>أمر البحث عن الحاويات التي تُركّب حجماً مُسمّى.</summary>
    public static string BuildVolumeLookup(string volumeName, bool sudo = false)
    {
        string d = DockerCli.Prefix(sudo);
        return d + "docker ps -aq 2>/dev/null | xargs -r " + d + "docker inspect --format '" + VolumeFormat + "' 2>/dev/null " +
           "| grep -F -- " + StorageScanner.ShellQuote(volumeName);
    }

    /// <summary>يحلّل مخرجات docker inspect المُصفّاة إلى <see cref="DockerLookup"/>.</summary>
    public static DockerLookup Parse(DockerPathKind kind, string key, string stdout)
    {
        var matches = new List<DockerContainerMatch>();
        foreach (var raw in (stdout ?? string.Empty).Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            var f = line.Split('|');
            if (f.Length < 5) continue;

            // طبقة الكتابة: المعرّف يظهر في UpperDir أو MergedDir (حصريّ للحاوية المالكة).
            bool writable = kind == DockerPathKind.Overlay
                && f.Length >= 7
                && (f[5].Contains(key, StringComparison.Ordinal) || f[6].Contains(key, StringComparison.Ordinal));

            matches.Add(new DockerContainerMatch(
                Name: f[0].TrimStart('/').Trim(),
                Status: f[1].Trim(),
                Image: f[2].Trim(),
                CoolifyName: f[3].Trim(),
                ComposeProject: f[4].Trim(),
                WritableLayer: writable));
        }
        return new DockerLookup(kind, key, matches);
    }

    /// <summary>يحدّد حاوية Docker المالكة لمسار ملفّ. لا يرمي — يُعيد نتيجة فارغة إن تعذّر التطابق.</summary>
    public async Task<DockerLookup> ResolveAsync(string path, CancellationToken ct = default)
    {
        var (kind, key) = Classify(path);
        if (kind == DockerPathKind.None)
            return new DockerLookup(kind, string.Empty, Array.Empty<DockerContainerMatch>());

        string cmd = kind == DockerPathKind.Overlay ? BuildOverlayLookup(key, _sudo) : BuildVolumeLookup(key, _sudo);
        var r = await _ssh.RunAsync(cmd, ct).ConfigureAwait(false);
        return Parse(kind, key, r.StdOut);
    }
}
