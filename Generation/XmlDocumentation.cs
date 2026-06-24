using System.Security;

namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class XmlDocumentation
{
    public static void AppendSummary(
        List<string> lines,
        string summary,
        int indentLevel = 0,
        bool allowXmlDocumentationMarkup = false)
    {
        var indent = new string(' ', indentLevel * 4);
        lines.Add($"{indent}/// <summary>");
        foreach (var line in Normalize(summary))
        {
            lines.Add($"{indent}/// {Escape(line, allowXmlDocumentationMarkup)}");
        }
        lines.Add($"{indent}/// </summary>");
    }

    public static void AppendRemarks(List<string> lines, IReadOnlyList<string> remarks, int indentLevel = 0)
    {
        if (remarks.Count == 0)
        {
            return;
        }

        var indent = new string(' ', indentLevel * 4);
        lines.Add($"{indent}/// <remarks>");
        foreach (var remark in remarks.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var line in Normalize(remark))
            {
                lines.Add($"{indent}/// {Escape(line)}");
            }
        }
        lines.Add($"{indent}/// </remarks>");
    }

    private static IEnumerable<string> Normalize(string text)
    {
        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line));
    }

    private static string Escape(string value, bool allowXmlDocumentationMarkup = false)
    {
        var escaped = SecurityElement.Escape(value) ?? string.Empty;
        return allowXmlDocumentationMarkup
            ? RestoreAllowedXmlDocumentationMarkup(escaped)
            : escaped;
    }

    private static string RestoreAllowedXmlDocumentationMarkup(string value)
    {
        return value
            .Replace("&lt;c&gt;", "<c>", StringComparison.Ordinal)
            .Replace("&lt;/c&gt;", "</c>", StringComparison.Ordinal)
            .Replace("&lt;see cref=&quot;", "<see cref=\"", StringComparison.Ordinal)
            .Replace("&quot;/&gt;", "\"/>", StringComparison.Ordinal);
    }
}
