using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class AbstractionsGenerator
{
    public static string Generate(NormalizedTelegramSchema schema, NormalizedTelegramAbstraction abstraction)
    {
        var lines = new List<string>();
        GeneratedFileHeader.Append(lines, schema.Metadata, "Abstraction");
        lines.Add(string.Empty);
        lines.Add("#nullable enable");

        if (abstraction.Kind == "union")
        {
            lines.Add("using System;");
            lines.Add("using System.Collections.Generic;");
            lines.Add("using System.Text.Json;");
            lines.Add("using System.Text.Json.Serialization;");
            lines.Add("using TeleFlow.Telegram.Schema.Types;");
        }

        lines.Add(string.Empty);
        lines.Add("namespace TeleFlow.Telegram.Schema.Abstractions;");
        lines.Add(string.Empty);

        XmlDocumentation.AppendSummary(lines, abstraction.Summary);

        switch (abstraction.Kind)
        {
            case "interface":
                lines.Add("public interface ITelegramApiMethod<TResult>");
                lines.Add("{");
                lines.Add("    static abstract string MethodName { get; }");
                lines.Add("}");
                break;
            case "union":
                UnionCodeGenerator.AppendUnion(
                    lines,
                    abstraction.Name,
                    abstraction.UnionCases.ToArray(),
                    abstraction.ValueShape);
                break;
            case "placeholder-type":
                lines.Add($"public sealed partial record class {abstraction.Name}");
                lines.Add("{");
                lines.Add("}");
                break;
        }

        return string.Join(Environment.NewLine, lines);
    }
}
