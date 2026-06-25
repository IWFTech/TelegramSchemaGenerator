using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class UnionCodeGenerator
{
    public static void AppendUnion(
        List<string> lines,
        string name,
        IReadOnlyList<NormalizedTelegramUnionCase> cases,
        string valueShape)
    {
        lines.Add($"[JsonConverter(typeof({name}JsonConverter))]");
        lines.Add($"public sealed partial record class {name}");
        lines.Add("{");

        foreach (var unionCase in cases)
        {
            var caseType = QualifyCaseType(unionCase.CSharpType);
            lines.Add($"    private {name}({caseType} value)");
            lines.Add("    {");
            if (RequiresNullGuard(unionCase.CSharpType))
            {
                lines.Add("        ArgumentNullException.ThrowIfNull(value);");
            }

            lines.Add($"        {unionCase.Name} = value;");
            lines.Add("    }");
            lines.Add(string.Empty);
        }

        foreach (var unionCase in cases)
        {
            XmlDocumentation.AppendSummary(lines, $"Gets the {unionCase.RawType} value when this instance represents that case.", indentLevel: 1);
            lines.Add($"    public {ToNullableCaseType(unionCase.CSharpType)} {unionCase.Name} {{ get; }}");
            lines.Add(string.Empty);
        }

        foreach (var unionCase in cases)
        {
            var caseType = QualifyCaseType(unionCase.CSharpType);
            XmlDocumentation.AppendSummary(
                lines,
                $"Creates a {name} value from {DescribeCaseType(unionCase)}.",
                indentLevel: 1,
                allowXmlDocumentationMarkup: true);
            lines.Add($"    public static {name} From({caseType} value)");
            lines.Add("    {");
            lines.Add($"        return new {name}(value);");
            lines.Add("    }");
            lines.Add(string.Empty);

            if (!IsInterfaceLikeCaseType(unionCase.CSharpType))
            {
                lines.Add($"    public static implicit operator {name}({caseType} value)");
                lines.Add("    {");
                lines.Add("        return From(value);");
                lines.Add("    }");
                lines.Add(string.Empty);
            }

            lines.Add($"    public bool TryGet{unionCase.Name}(out {ToNullableCaseType(unionCase.CSharpType)} value)");
            lines.Add("    {");
            lines.Add($"        value = {unionCase.Name};");
            lines.Add("        return value is not null;");
            lines.Add("    }");
            lines.Add(string.Empty);
        }

        lines.Add("}");
        lines.Add(string.Empty);
        lines.Add($"file sealed class {name}JsonConverter : JsonConverter<{name}>");
        lines.Add("{");
        lines.Add($"    public override {name} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
        lines.Add("    {");
        AppendReadBody(lines, name, cases);
        lines.Add("    }");
        lines.Add(string.Empty);
        lines.Add($"    public override void Write(Utf8JsonWriter writer, {name} value, JsonSerializerOptions options)");
        lines.Add("    {");
        lines.Add("        ArgumentNullException.ThrowIfNull(value);");

        foreach (var unionCase in cases)
        {
            lines.Add($"        if (value.{unionCase.Name} is not null)");
            lines.Add("        {");
            lines.Add($"            JsonSerializer.Serialize(writer, value.{unionCase.Name}, options);");
            lines.Add("            return;");
            lines.Add("        }");
            lines.Add(string.Empty);
        }

        lines.Add($"        throw new JsonException(\"The {name} instance does not contain a union case value.\");");
        lines.Add("    }");
        lines.Add("}");
    }

    private static void AppendReadBody(
        List<string> lines,
        string name,
        IReadOnlyList<NormalizedTelegramUnionCase> cases)
    {
        foreach (var unionCase in cases)
        {
            switch (unionCase.MatchStrategy)
            {
                case "string-token":
                    lines.Add("        if (reader.TokenType == JsonTokenType.String)");
                    lines.Add("        {");
                    lines.Add($"            return {name}.From(reader.GetString() ?? throw new JsonException(\"Expected a string value.\"));");
                    lines.Add("        }");
                    lines.Add(string.Empty);
                    break;
                case "integer-token":
                    lines.Add("        if (reader.TokenType == JsonTokenType.Number)");
                    lines.Add("        {");
                    lines.Add($"            return {name}.From(reader.GetInt64());");
                    lines.Add("        }");
                    lines.Add(string.Empty);
                    break;
                case "boolean-token":
                    lines.Add("        if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)");
                    lines.Add("        {");
                    lines.Add($"            return {name}.From(reader.GetBoolean());");
                    lines.Add("        }");
                    lines.Add(string.Empty);
                    break;
                case "float-token":
                    lines.Add("        if (reader.TokenType == JsonTokenType.Number)");
                    lines.Add("        {");
                    lines.Add($"            return {name}.From(reader.GetDouble());");
                    lines.Add("        }");
                    lines.Add(string.Empty);
                    break;
                case "array-token":
                    lines.Add("        if (reader.TokenType == JsonTokenType.StartArray)");
                    lines.Add("        {");
                    lines.Add($"            return {name}.From(JsonSerializer.Deserialize<{QualifyCaseType(unionCase.CSharpType)}>(ref reader, options)");
                    lines.Add($"                ?? throw new JsonException(\"Unable to deserialize {name} as {unionCase.RawType}.\"));");
                    lines.Add("        }");
                    lines.Add(string.Empty);
                    break;
            }
        }

        lines.Add("        if (reader.TokenType != JsonTokenType.StartObject)");
        lines.Add("        {");
        lines.Add($"            throw new JsonException(\"Unable to deserialize {name}: unexpected JSON token.\");");
        lines.Add("        }");
        lines.Add(string.Empty);
        lines.Add("        using var document = JsonDocument.ParseValue(ref reader);");
        lines.Add("        var json = document.RootElement.GetRawText();");
        lines.Add(string.Empty);

        AppendPropertyDiscriminatorCases(lines, name, cases);
        AppendDateZeroCases(lines, name, cases);
        AppendRequiredPropertyCases(lines, name, cases);
        AppendFallbackObjectCases(lines, name, cases);

        lines.Add($"        throw new JsonException(\"Unable to deserialize {name} from the provided Telegram payload.\");");
    }

    private static void AppendPropertyDiscriminatorCases(
        List<string> lines,
        string name,
        IReadOnlyList<NormalizedTelegramUnionCase> cases)
    {
        foreach (var group in cases
                     .Where(static unionCase => unionCase.MatchStrategy == "property-discriminator")
                     .GroupBy(static unionCase => unionCase.DiscriminatorProperty, StringComparer.Ordinal))
        {
            var propertyName = group.Key
                ?? throw new InvalidOperationException($"Union '{name}' has discriminator cases without discriminator property metadata.");

            lines.Add($"        if (document.RootElement.TryGetProperty(\"{propertyName}\", out var {propertyName}Element) && {propertyName}Element.ValueKind == JsonValueKind.String)");
            lines.Add("        {");
            lines.Add($"            var discriminator = {propertyName}Element.GetString();");
            lines.Add("            switch (discriminator)");
            lines.Add("            {");

            foreach (var discriminatorGroup in group
                         .GroupBy(static unionCase => unionCase.DiscriminatorValue, StringComparer.Ordinal)
                         .OrderBy(static discriminatorGroup => discriminatorGroup.Key, StringComparer.Ordinal))
            {
                lines.Add($"                case \"{discriminatorGroup.Key}\":");
                if (discriminatorGroup.Count() == 1)
                {
                    var unionCase = discriminatorGroup.Single();
                    lines.Add($"                    return {name}.From(JsonSerializer.Deserialize<{QualifyCaseType(unionCase.CSharpType)}>(json, options)");
                    lines.Add($"                        ?? throw new JsonException(\"Unable to deserialize {name} as {unionCase.RawType}.\"));");
                }
                else
                {
                    foreach (var unionCase in discriminatorGroup
                                 .OrderByDescending(static unionCase => unionCase.RequiredProperties.Count)
                                 .ThenBy(static unionCase => unionCase.RawType, StringComparer.Ordinal))
                    {
                        var checks = string.Join(
                            " && ",
                            unionCase.RequiredProperties.Select(static property => $"document.RootElement.TryGetProperty(\"{property}\", out _)"));
                        lines.Add($"                    if ({checks})");
                        lines.Add("                    {");
                        lines.Add($"                        return {name}.From(JsonSerializer.Deserialize<{QualifyCaseType(unionCase.CSharpType)}>(json, options)");
                        lines.Add($"                            ?? throw new JsonException(\"Unable to deserialize {name} as {unionCase.RawType}.\"));");
                        lines.Add("                    }");
                    }

                    lines.Add($"                    throw new JsonException(\"Unable to refine discriminator value '{discriminatorGroup.Key}' for {name} using required properties.\");");
                }
            }

            lines.Add("                default:");
            lines.Add($"                    throw new JsonException($\"Unknown discriminator value '{{discriminator}}' for {name}.\");");
            lines.Add("            }");
            lines.Add("        }");
            lines.Add(string.Empty);
        }
    }

    private static void AppendRequiredPropertyCases(
        List<string> lines,
        string name,
        IReadOnlyList<NormalizedTelegramUnionCase> cases)
    {
        foreach (var unionCase in cases
                     .Where(static unionCase => unionCase.MatchStrategy == "required-properties")
                     .OrderByDescending(static unionCase => unionCase.RequiredProperties.Count)
                     .ThenBy(static unionCase => unionCase.RawType, StringComparer.Ordinal))
        {
            if (unionCase.RequiredProperties.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Union '{name}' case '{unionCase.RawType}' is missing required-property match metadata.");
            }

            var checks = string.Join(
                " && ",
                unionCase.RequiredProperties.Select(static property => $"document.RootElement.TryGetProperty(\"{property}\", out _)"));

            lines.Add($"        if ({checks})");
            lines.Add("        {");
            lines.Add($"            return {name}.From(JsonSerializer.Deserialize<{QualifyCaseType(unionCase.CSharpType)}>(json, options)");
            lines.Add($"                ?? throw new JsonException(\"Unable to deserialize {name} as {unionCase.RawType}.\"));");
            lines.Add("        }");
            lines.Add(string.Empty);
        }
    }

    private static void AppendDateZeroCases(
        List<string> lines,
        string name,
        IReadOnlyList<NormalizedTelegramUnionCase> cases)
    {
        foreach (var unionCase in cases.Where(static unionCase => unionCase.MatchStrategy == "date-zero"))
        {
            lines.Add("        if (document.RootElement.TryGetProperty(\"date\", out var dateElement) &&");
            lines.Add("            dateElement.ValueKind == JsonValueKind.Number &&");
            lines.Add("            dateElement.TryGetInt64(out var date) &&");
            lines.Add("            date == 0)");
            lines.Add("        {");
            lines.Add($"            return {name}.From(JsonSerializer.Deserialize<{QualifyCaseType(unionCase.CSharpType)}>(json, options)");
            lines.Add($"                ?? throw new JsonException(\"Unable to deserialize {name} as {unionCase.RawType}.\"));");
            lines.Add("        }");
            lines.Add(string.Empty);
        }
    }

    private static void AppendFallbackObjectCases(
        List<string> lines,
        string name,
        IReadOnlyList<NormalizedTelegramUnionCase> cases)
    {
        foreach (var unionCase in cases.Where(static unionCase => unionCase.MatchStrategy == "fallback-object"))
        {
            lines.Add($"        return {name}.From(JsonSerializer.Deserialize<{QualifyCaseType(unionCase.CSharpType)}>(json, options)");
            lines.Add($"            ?? throw new JsonException(\"Unable to deserialize {name} as {unionCase.RawType}.\"));");
        }
    }

    private static string QualifyCaseType(string typeName)
    {
        if (typeName.StartsWith("IReadOnlyList<", StringComparison.Ordinal) && typeName.EndsWith('>'))
        {
            var inner = typeName["IReadOnlyList<".Length..^1];
            return $"IReadOnlyList<{QualifyCaseType(inner)}>";
        }

        return typeName switch
        {
            "long" or "bool" or "double" or "string" => typeName,
            _ => typeName
        };
    }

    private static string ToNullableCaseType(string typeName)
    {
        return typeName switch
        {
            "long" => "long?",
            "bool" => "bool?",
            "double" => "double?",
            "string" => "string?",
            _ => QualifyCaseType(typeName) + "?"
        };
    }

    private static bool RequiresNullGuard(string typeName)
    {
        return typeName is "string" || !IsPrimitiveMember(typeName);
    }

    private static bool IsPrimitiveMember(string typeName)
    {
        return typeName is "long" or "bool" or "double";
    }

    private static bool IsInterfaceLikeCaseType(string typeName)
    {
        return typeName.StartsWith("IReadOnlyList<", StringComparison.Ordinal);
    }

    private static string DescribeCaseType(NormalizedTelegramUnionCase unionCase)
    {
        return unionCase.CSharpType switch
        {
            "long" => "<c>long</c>",
            "string" => "<c>string</c>",
            "bool" => "<c>bool</c>",
            "double" => "<c>double</c>",
            _ => $"<c>{unionCase.CSharpType}</c>"
        };
    }
}
