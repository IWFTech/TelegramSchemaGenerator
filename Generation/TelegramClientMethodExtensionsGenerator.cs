using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class TelegramClientMethodExtensionsGenerator
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte",
        "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while"
    };

    public static string Generate(
        NormalizedTelegramSchema schema,
        NormalizedTelegramMethod method,
        IReadOnlySet<string> abstractionNames)
    {
        var lines = new List<string>();
        GeneratedFileHeader.Append(lines, schema.Metadata, "ClientMethod");
        lines.Add(string.Empty);
        lines.Add("#nullable enable");

        if (method.ResultType == "File" || method.Parameters.Any(static parameter => ContainsType(parameter.CSharpType, "File")))
        {
            lines.Add("using TelegramFile = TeleFlow.Telegram.Schema.Types.File;");
        }

        lines.Add("using TeleFlow.Telegram.Schema.Abstractions;");
        lines.Add("using TeleFlow.Telegram.Schema.Methods;");
        lines.Add("using TeleFlow.Telegram.Schema.Types;");
        if (method.Parameters.Any(IsParseModeParameter))
        {
            lines.Add("using TeleFlow.Telegram.Internal;");
        }

        lines.Add(string.Empty);
        lines.Add("namespace TeleFlow.Telegram;");
        lines.Add(string.Empty);
        lines.Add($"public static class TelegramClient{method.Name}Extensions");
        lines.Add("{");

        XmlDocumentation.AppendSummary(lines, method.Summary, indentLevel: 1);
        var resultType = QualifyType(method.ResultType, abstractionNames);
        var parameters = OrderParameters(method.Parameters).ToArray();
        var signatureParameters = parameters
            .Select(parameter => FormatSignatureParameter(parameter, abstractionNames))
            .Prepend("this ITelegramClient bot")
            .Append("CancellationToken cancellationToken = default");

        lines.Add($"    public static Task<{resultType}> {method.Name}Async(");
        lines.Add(string.Join("," + Environment.NewLine, signatureParameters.Select(static parameter => $"        {parameter}")) + ")");
        lines.Add("    {");
        lines.Add("        ArgumentNullException.ThrowIfNull(bot);");
        lines.Add(string.Empty);
        lines.Add("        return bot.SendAsync(");
        lines.Add($"            new {method.Name}");
        lines.Add("            {");

        foreach (var parameter in parameters)
        {
            lines.Add($"                {parameter.Name} = {FormatInitializerValue(method, parameter)},");
        }

        lines.Add("            },");
        lines.Add("            cancellationToken);");
        lines.Add("    }");
        lines.Add("}");

        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<NormalizedTelegramProperty> OrderParameters(IReadOnlyList<NormalizedTelegramProperty> parameters)
    {
        return parameters
            .Where(static parameter => parameter.Required)
            .Concat(parameters.Where(static parameter => !parameter.Required));
    }

    private static string FormatSignatureParameter(
        NormalizedTelegramProperty parameter,
        IReadOnlySet<string> abstractionNames)
    {
        var parameterType = IsParseModeParameter(parameter)
            ? "TelegramParseMode?"
            : QualifyType(parameter.CSharpType, abstractionNames);
        var parameterName = GetParameterName(parameter.Name);
        return parameter.Required
            ? $"{parameterType} {parameterName}"
            : $"{parameterType} {parameterName} = null";
    }

    private static string FormatInitializerValue(
        NormalizedTelegramMethod method,
        NormalizedTelegramProperty parameter)
    {
        var parameterName = GetParameterName(parameter.Name);
        if (!IsParseModeParameter(parameter))
        {
            if (parameter.Name == "LinkPreviewOptions")
            {
                return $"{parameterName} ?? bot.Defaults.LinkPreviewOptions";
            }

            if (parameter.Name == "DisableNotification")
            {
                return $"{parameterName} ?? bot.Defaults.DisableNotification";
            }

            if (parameter.Name == "ProtectContent")
            {
                return $"{parameterName} ?? bot.Defaults.ProtectContent";
            }

            return parameterName;
        }

        var entitiesParameter = method.Parameters.FirstOrDefault(IsEntityCollectionParameter);
        return entitiesParameter is null
            ? $"TelegramMethodDefaultResolver.ResolveParseMode(bot, {parameterName})"
            : $"TelegramMethodDefaultResolver.ResolveParseMode(bot, {parameterName}, {GetParameterName(entitiesParameter.Name)})";
    }

    private static bool IsParseModeParameter(NormalizedTelegramProperty parameter)
    {
        return parameter.Name == "ParseMode" && parameter.CSharpType == "string?";
    }

    private static bool IsEntityCollectionParameter(NormalizedTelegramProperty parameter)
    {
        return parameter.Name is "Entities" or "CaptionEntities" &&
            parameter.CSharpType == "IReadOnlyList<MessageEntity>?";
    }

    private static string GetParameterName(string propertyName)
    {
        var name = char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
        return CSharpKeywords.Contains(name) ? "@" + name : name;
    }

    private static bool ContainsType(string typeName, string expectedType)
    {
        if (typeName.EndsWith('?'))
        {
            return ContainsType(typeName[..^1], expectedType);
        }

        if (typeName.StartsWith("IReadOnlyList<", StringComparison.Ordinal) && typeName.EndsWith('>'))
        {
            return ContainsType(typeName["IReadOnlyList<".Length..^1], expectedType);
        }

        return typeName == expectedType;
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
