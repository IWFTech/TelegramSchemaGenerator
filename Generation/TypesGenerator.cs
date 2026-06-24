using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class TypesGenerator
{
    public static string Generate(
        NormalizedTelegramSchema schema,
        NormalizedTelegramType type,
        IReadOnlySet<string> abstractionNames)
    {
        var lines = new List<string>();
        GeneratedFileHeader.Append(lines, schema.Metadata, "Type");
        lines.Add(string.Empty);
        lines.Add("#nullable enable");
        lines.Add("using System.Text.Json.Serialization;");
        if (type.Properties.Any(property => UsesAbstraction(property.CSharpType, abstractionNames)))
        {
            lines.Add("using TeleFlow.Telegram.Schema.Abstractions;");
        }

        if (type.Kind == "named-union")
        {
            lines.Add("using System;");
            lines.Add("using System.Collections.Generic;");
            lines.Add("using System.Text.Json;");
        }
        else if (type.Kind == "pseudo-upload")
        {
            lines.Add("using System;");
            lines.Add("using System.IO;");
            lines.Add("using System.Text.Json;");
        }
        else if (type.Properties.Any(static property => property.LiteralValue is not null))
        {
            lines.Add("using System.Text.Json;");
        }

        lines.Add(string.Empty);
        lines.Add("namespace TeleFlow.Telegram.Schema.Types;");
        lines.Add(string.Empty);

        XmlDocumentation.AppendSummary(lines, type.Summary);
        var remarks = type.Remarks.ToList();
        if (type.UnionMembers.Count > 0)
        {
            remarks.Add("Union members: " + string.Join(", ", type.UnionMembers) + ".");
        }
        XmlDocumentation.AppendRemarks(lines, remarks);
        if (type.Kind == "named-union")
        {
            UnionCodeGenerator.AppendUnion(lines, type.Name, type.UnionCases, "named-union");
        }
        else if (type.Kind == "pseudo-upload")
        {
            AppendInputFile(lines);
        }
        else
        {
            var hasLiteralProperties = type.Properties.Any(static property => property.LiteralValue is not null);
            lines.Add($"public sealed partial record class {type.Name}{(hasLiteralProperties ? " : IJsonOnDeserialized" : string.Empty)}");
            lines.Add("{");

            foreach (var property in type.Properties)
            {
                XmlDocumentation.AppendSummary(lines, property.Summary, indentLevel: 1);
                if (property.LiteralValue is not null)
                {
                    lines.Add($"    public const {QualifyType(property.CSharpType, abstractionNames)} {property.Name}Value = {FormatLiteral(property)};");
                    lines.Add(string.Empty);
                }

                lines.Add($"    [JsonPropertyName(\"{property.TelegramName}\")]");
                if (property.Required)
                {
                    lines.Add("    [JsonRequired]");
                }

                if (!property.Required)
                {
                    lines.Add("    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]");
                }

                var propertyType = QualifyType(property.CSharpType, abstractionNames);
                lines.Add($"    public {GetRequiredKeyword(propertyType, property)}{propertyType} {property.Name} {{ get; init; }}{GetInitializer(propertyType, property)}");
                lines.Add(string.Empty);
            }

            if (hasLiteralProperties)
            {
                lines.Add("    void IJsonOnDeserialized.OnDeserialized()");
                lines.Add("    {");
                foreach (var property in type.Properties.Where(static property => property.LiteralValue is not null))
                {
                    lines.Add($"        if ({property.Name} != {property.Name}Value)");
                    lines.Add("        {");
                    lines.Add($"            throw new JsonException(\"Expected Telegram literal '{property.LiteralValue}' for field '{property.TelegramName}' on {type.Name}.\");");
                    lines.Add("        }");
                    lines.Add(string.Empty);
                }

                lines.Add("    }");
            }

            lines.Add("}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendInputFile(List<string> lines)
    {
        lines.Add("[JsonConverter(typeof(InputFileJsonConverter))]");
        lines.Add("public sealed partial record class InputFile");
        lines.Add("{");
        lines.Add("    private InputFile(Stream content, string fileName)");
        lines.Add("    {");
        lines.Add("        ArgumentNullException.ThrowIfNull(content);");
        lines.Add("        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);");
        lines.Add("        Content = content;");
        lines.Add("        FileName = fileName;");
        lines.Add("    }");
        lines.Add(string.Empty);
        lines.Add("    public Stream Content { get; }");
        lines.Add(string.Empty);
        lines.Add("    public string FileName { get; }");
        lines.Add(string.Empty);
        lines.Add("    public static InputFile FromStream(Stream content, string fileName)");
        lines.Add("    {");
        lines.Add("        return new InputFile(content, fileName);");
        lines.Add("    }");
        lines.Add("}");
        lines.Add(string.Empty);
        lines.Add("file sealed class InputFileJsonConverter : JsonConverter<InputFile>");
        lines.Add("{");
        lines.Add("    public override InputFile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
        lines.Add("    {");
        lines.Add("        throw new JsonException(\"InputFile values are upload-only and cannot be read from Telegram JSON payloads.\");");
        lines.Add("    }");
        lines.Add(string.Empty);
        lines.Add("    public override void Write(Utf8JsonWriter writer, InputFile value, JsonSerializerOptions options)");
        lines.Add("    {");
        lines.Add("        throw new JsonException(\"InputFile values must be sent using multipart/form-data by the Telegram transport executor.\");");
        lines.Add("    }");
        lines.Add("}");
    }

    private static string GetRequiredKeyword(string csharpType, NormalizedTelegramProperty property)
    {
        if (!property.Required || property.LiteralValue is not null)
        {
            return string.Empty;
        }

        return !IsNullableValueType(csharpType) ? "required " : string.Empty;
    }

    private static string GetInitializer(string csharpType, NormalizedTelegramProperty property)
    {
        if (property.LiteralValue is not null)
        {
            return $" = {property.Name}Value;";
        }

        if (!property.Required)
        {
            return string.Empty;
        }

        return csharpType switch
        {
            "string" => " = null!;",
            _ when !IsNullableValueType(csharpType) && csharpType is not "long" and not "bool" and not "double" => " = null!;",
            _ => string.Empty
        };
    }

    private static string FormatLiteral(NormalizedTelegramProperty property)
    {
        var value = property.LiteralValue ?? throw new InvalidOperationException($"Property '{property.Name}' does not have literal metadata.");
        var csharpType = property.CSharpType.TrimEnd('?');

        return csharpType switch
        {
            "long" => value,
            "bool" => value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false",
            _ => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
        };
    }

    private static bool IsNullableValueType(string typeName)
    {
        return typeName.EndsWith('?');
    }

    private static string QualifyType(string typeName, IReadOnlySet<string> abstractionNames)
    {
        if (typeName.EndsWith('?'))
        {
            return QualifyType(typeName[..^1], abstractionNames) + "?";
        }

        if (typeName.StartsWith("IReadOnlyList<", StringComparison.Ordinal) && typeName.EndsWith('>'))
        {
            var inner = typeName["IReadOnlyList<".Length..^1];
            return $"IReadOnlyList<{QualifyType(inner, abstractionNames)}>";
        }

        if (typeName is "long" or "bool" or "double" or "string" or "object")
        {
            return typeName;
        }

        if (abstractionNames.Contains(typeName))
        {
            return typeName;
        }

        return typeName;
    }

    private static bool UsesAbstraction(string typeName, IReadOnlySet<string> abstractionNames)
    {
        if (typeName.EndsWith('?'))
        {
            return UsesAbstraction(typeName[..^1], abstractionNames);
        }

        if (typeName.StartsWith("IReadOnlyList<", StringComparison.Ordinal) && typeName.EndsWith('>'))
        {
            var inner = typeName["IReadOnlyList<".Length..^1];
            return UsesAbstraction(inner, abstractionNames);
        }

        return abstractionNames.Contains(typeName);
    }
}
