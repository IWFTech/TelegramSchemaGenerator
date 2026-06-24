using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class TelegramUpdateTypesGenerator
{
    public static string Generate(NormalizedTelegramSchema schema)
    {
        var updateType = schema.Types.FirstOrDefault(static type => type.Name == "Update")
                         ?? throw new InvalidOperationException("The normalized schema does not contain the Update type.");

        var updateProperties = updateType.Properties
            .Where(static property => property.TelegramName != "update_id")
            .ToArray();

        if (updateProperties.Length == 0)
        {
            throw new InvalidOperationException("The Update type does not contain any update payload properties.");
        }

        var lines = new List<string>();
        GeneratedFileHeader.Append(lines, schema.Metadata, "UpdateType");
        lines.Add(string.Empty);
        lines.Add("#nullable enable");
        lines.Add(string.Empty);
        lines.Add("namespace TeleFlow.Telegram;");
        lines.Add(string.Empty);
        lines.Add("public readonly partial record struct TelegramUpdateType");
        lines.Add("{");

        foreach (var property in updateProperties)
        {
            XmlDocumentation.AppendSummary(lines, property.Summary, indentLevel: 1);
            lines.Add($"    public static TelegramUpdateType {property.Name} => new(\"{property.TelegramName}\");");
            lines.Add(string.Empty);
        }

        lines.Add("    public static IReadOnlyList<TelegramUpdateType> AllKnown { get; } =");
        lines.Add("    [");

        foreach (var property in updateProperties)
        {
            lines.Add($"        {property.Name},");
        }

        lines.Add("    ];");
        lines.Add("}");

        return string.Join(Environment.NewLine, lines);
    }
}
