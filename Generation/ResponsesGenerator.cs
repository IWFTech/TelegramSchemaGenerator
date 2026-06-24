using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class ResponsesGenerator
{
    public static string Generate(NormalizedTelegramSchema schema)
    {
        var lines = new List<string>();
        GeneratedFileHeader.Append(lines, schema.Metadata, "Response");
        lines.Add(string.Empty);
        lines.Add("#nullable enable");
        lines.Add("using System.Text.Json.Serialization;");
        lines.Add(string.Empty);
        lines.Add("namespace TeleFlow.Telegram.Schema.Responses;");
        lines.Add(string.Empty);

        XmlDocumentation.AppendSummary(lines, "Represents a raw Telegram Bot API response envelope.");
        lines.Add("public sealed partial record class TelegramApiResponse<TResult>");
        lines.Add("{");
        lines.Add("    [JsonPropertyName(\"ok\")]");
        lines.Add("    public required bool Ok { get; init; }");
        lines.Add(string.Empty);
        lines.Add("    [JsonPropertyName(\"result\")]");
        lines.Add("    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]");
        lines.Add("    public TResult? Result { get; init; }");
        lines.Add(string.Empty);
        lines.Add("    [JsonPropertyName(\"description\")]");
        lines.Add("    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]");
        lines.Add("    public string? Description { get; init; }");
        lines.Add("}");

        return string.Join(Environment.NewLine, lines);
    }
}
