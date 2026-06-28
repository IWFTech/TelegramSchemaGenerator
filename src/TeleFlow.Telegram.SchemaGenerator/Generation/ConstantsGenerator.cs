using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class ConstantsGenerator
{
    public static string Generate(NormalizedTelegramSchema schema, NormalizedTelegramConstantGroup group)
    {
        var lines = new List<string>();
        GeneratedFileHeader.Append(lines, schema.Metadata, "ConstantGroup");
        lines.Add(string.Empty);
        lines.Add("#nullable enable");
        lines.Add(string.Empty);
        lines.Add("namespace TeleFlow.Telegram.Schema.Constants;");
        lines.Add(string.Empty);

        XmlDocumentation.AppendSummary(lines, group.Summary);
        lines.Add($"public static class {group.Name}");
        lines.Add("{");

        for (var index = 0; index < group.Values.Count; index++)
        {
            var value = group.Values[index];
            XmlDocumentation.AppendSummary(
                lines,
                $"Telegram Bot API value <c>{value.TelegramValue}</c>.",
                indentLevel: 1,
                allowXmlDocumentationMarkup: true);
            lines.Add($"    public const string {value.Name} = \"{EscapeStringLiteral(value.TelegramValue)}\";");

            if (index + 1 < group.Values.Count)
            {
                lines.Add(string.Empty);
            }
        }

        lines.Add("}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeStringLiteral(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
