using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class MethodsGenerator
{
    public static string Generate(NormalizedTelegramSchema schema, NormalizedTelegramMethod method, IReadOnlySet<string> abstractionNames)
    {
        var lines = new List<string>();
        GeneratedFileHeader.Append(lines, schema.Metadata, "Method");
        lines.Add(string.Empty);
        lines.Add("#nullable enable");
        if (method.ResultType == "File")
        {
            lines.Add("using TelegramFile = TeleFlow.Telegram.Schema.Types.File;");
        }

        lines.Add("using System.Text.Json.Serialization;");
        lines.Add("using TeleFlow.Telegram.Schema.Abstractions;");
        lines.Add("using TeleFlow.Telegram.Schema.Types;");
        lines.Add(string.Empty);
        lines.Add("namespace TeleFlow.Telegram.Schema.Methods;");
        lines.Add(string.Empty);

        XmlDocumentation.AppendSummary(lines, method.Summary);
        var remarks = method.Remarks.ToList();
        if (!string.IsNullOrWhiteSpace(method.RawResultType))
        {
            remarks.Insert(0, $"Telegram result type: {method.RawResultType}.");
        }
        XmlDocumentation.AppendRemarks(lines, remarks);
        var resultType = QualifyType(method.ResultType, abstractionNames);
        lines.Add($"public sealed partial record class {method.Name} : ITelegramApiMethod<{resultType}>");
        lines.Add("{");
        lines.Add($"    public static string MethodName => \"{method.TelegramMethodName}\";");
        lines.Add(string.Empty);

        foreach (var parameter in method.Parameters)
        {
            XmlDocumentation.AppendSummary(lines, parameter.Summary, indentLevel: 1);
            lines.Add($"    [JsonPropertyName(\"{parameter.TelegramName}\")]");
            if (parameter.Required)
            {
                lines.Add("    [JsonRequired]");
            }

            if (!parameter.Required)
            {
                lines.Add("    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]");
            }

            var parameterType = QualifyType(parameter.CSharpType, abstractionNames);
            lines.Add($"    public {GetRequiredKeyword(parameterType, parameter.Required)}{parameterType} {parameter.Name} {{ get; init; }}{GetInitializer(parameterType, parameter.Required)}");
            lines.Add(string.Empty);
        }

        lines.Add("}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string GetRequiredKeyword(string csharpType, bool required)
    {
        if (!required)
        {
            return string.Empty;
        }

        return csharpType switch
        {
            _ when !csharpType.EndsWith('?') => "required ",
            _ => string.Empty
        };
    }

    private static string GetInitializer(string csharpType, bool required)
    {
        if (!required)
        {
            return string.Empty;
        }

        return csharpType switch
        {
            "string" => " = null!;",
            _ when !csharpType.EndsWith('?') &&
                   csharpType is not "long" and not "bool" and not "double" => " = null!;",
            _ => string.Empty
        };
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

        if (typeName == "File")
        {
            return "TelegramFile";
        }

        if (abstractionNames.Contains(typeName))
        {
            return typeName;
        }

        return typeName;
    }
}
