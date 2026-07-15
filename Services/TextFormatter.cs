using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;

namespace TerminalLauncher.Services;

/// <summary>
/// أوتو-فورمات النصّ عند الحفظ: يُجمّل JSON/XML حسب الامتداد، ويهذّب النصّ العامّ (مسافات ذيليّة/أسطر).
/// إن فشل التحليل (تنسيق غير صالح) يعود للتهذيب البسيط فلا يُفسِد الملفّ.
/// </summary>
public static class TextFormatter
{
    private static readonly JsonSerializerOptions JsonPretty = new()
    {
        WriteIndented = true,
        // نُبقي المحارف غير اللاتينيّة (العربيّة مثلاً) كما هي بدل \uXXXX
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>ينسّق النصّ حسب امتداد المسار؛ يُبقي الأصل إن كان التنسيق غير صالح.</summary>
    public static string Auto(string text, string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            switch (ext)
            {
                case ".json":
                    return FormatJson(text);
                case ".xml": case ".config": case ".csproj": case ".props":
                case ".targets": case ".svg": case ".xaml": case ".plist":
                    return FormatXml(text);
            }
        }
        catch { return NormalizeWhitespace(text); }   // تحليل فاشل → تهذيب فقط (لا نكسر الملفّ)
        return NormalizeWhitespace(text);
    }

    private static string FormatJson(string text)
    {
        var opts = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        using var doc = JsonDocument.Parse(text, opts);
        return JsonSerializer.Serialize(doc.RootElement, JsonPretty) + "\n";
    }

    private static string FormatXml(string text)
        => XDocument.Parse(text).ToString(SaveOptions.None) + "\n";

    /// <summary>تهذيب عامّ: إزالة المسافات الذيليّة لكلّ سطر + توحيد الأسطر (LF) + سطر أخير واحد.</summary>
    public static string NormalizeWhitespace(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            sb.Append(lines[i].TrimEnd());
            if (i < lines.Length - 1) sb.Append('\n');
        }
        string s = sb.ToString().TrimEnd('\n');
        return s.Length == 0 ? s : s + "\n";
    }
}
