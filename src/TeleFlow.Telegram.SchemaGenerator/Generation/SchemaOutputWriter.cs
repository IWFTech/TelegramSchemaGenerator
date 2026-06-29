using System.Text;
using System.Text.RegularExpressions;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class SchemaOutputWriter
{
    public static void Write(string outputDirectory, NormalizedTelegramSchema schema)
    {
        Validate(schema);

        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);

        DeleteLegacyFiles(root);
        RecreateDirectory(Path.Combine(root, "Types"));
        RecreateDirectory(Path.Combine(root, "Methods"));
        RecreateDirectory(Path.Combine(root, "Responses"));
        RecreateDirectory(Path.Combine(root, "Abstractions"));
        RecreateDirectory(Path.Combine(root, "Constants"));

        File.WriteAllText(
            Path.Combine(root, "Responses", "TelegramApiResponse.g.cs"),
            ResponsesGenerator.Generate(schema),
            Utf8WithoutBom.Instance);

        var abstractionNames = schema.Abstractions
            .Select(static abstraction => abstraction.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var abstraction in schema.Abstractions
                     .Where(static item => item.Kind is not "response-envelope")
                     .OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            File.WriteAllText(
                Path.Combine(root, "Abstractions", abstraction.Name + ".g.cs"),
                AbstractionsGenerator.Generate(schema, abstraction),
                Utf8WithoutBom.Instance);
        }

        foreach (var type in schema.Types.OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            File.WriteAllText(
                Path.Combine(root, "Types", type.Name + ".g.cs"),
                TypesGenerator.Generate(schema, type, abstractionNames),
                Utf8WithoutBom.Instance);
        }

        foreach (var method in schema.Methods.OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            File.WriteAllText(
                Path.Combine(root, "Methods", method.Name + ".g.cs"),
                MethodsGenerator.Generate(schema, method, abstractionNames),
                Utf8WithoutBom.Instance);
        }

        foreach (var group in schema.ConstantGroups.OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            File.WriteAllText(
                Path.Combine(root, "Constants", group.Name + ".g.cs"),
                ConstantsGenerator.Generate(schema, group),
                Utf8WithoutBom.Instance);
        }

        TelegramBotApiManifestWriter.Write(root, schema.Metadata);
    }

    private static void Validate(NormalizedTelegramSchema schema)
    {
        var duplicateNames = schema.Types.Select(static item => item.Name)
            .Concat(schema.Methods.Select(static item => item.Name))
            .Concat(schema.Abstractions
                .Where(static item => item.Kind is not "interface" and not "response-envelope")
                .Select(static item => item.Name))
            .Concat(schema.ConstantGroups.Select(static item => item.Name))
            .GroupBy(static item => item, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicateNames.Length > 0)
        {
            throw new InvalidOperationException("Duplicate generated schema names were detected: " + string.Join(", ", duplicateNames));
        }

        var invalidMethodNames = schema.Methods
            .Where(static method => method.Name != ToPascalCase(method.TelegramMethodName))
            .Select(static method => method.Name)
            .ToArray();

        if (invalidMethodNames.Length > 0)
        {
            throw new InvalidOperationException(
                "Generated method names must match the canonical Telegram method name without synthetic suffixes: " +
                string.Join(", ", invalidMethodNames));
        }

        var invalidAbstractionNames = schema.Abstractions
            .Where(static abstraction => abstraction.Kind == "union")
            .Select(static abstraction => abstraction.Name)
            .Where(static name => name is "Unknown" || name.Length > 80 || Regex.IsMatch(name, @"Union[0-9A-F]{6}$", RegexOptions.CultureInvariant))
            .ToArray();

        if (invalidAbstractionNames.Length > 0)
        {
            throw new InvalidOperationException("Generated abstraction names failed quality checks: " + string.Join(", ", invalidAbstractionNames));
        }

        var duplicateConstantGroupNames = schema.ConstantGroups
            .GroupBy(static group => group.Name, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicateConstantGroupNames.Length > 0)
        {
            throw new InvalidOperationException("Duplicate generated constant group names were detected: " + string.Join(", ", duplicateConstantGroupNames));
        }

        foreach (var group in schema.ConstantGroups)
        {
            var duplicateConstantNames = group.Values
                .GroupBy(static value => value.Name, StringComparer.Ordinal)
                .Where(static valueGroup => valueGroup.Count() > 1)
                .Select(static valueGroup => valueGroup.Key)
                .ToArray();

            if (duplicateConstantNames.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Duplicate generated constant names were detected for '{group.Name}': " + string.Join(", ", duplicateConstantNames));
            }
        }
    }

    private static string ToPascalCase(string value)
    {
        var parts = value.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static void DeleteLegacyFiles(string root)
    {
        foreach (var fileName in new[] { "GeneratedModels.g.cs", "GeneratedMethods.g.cs" })
        {
            var path = Path.Combine(root, fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

internal static class Utf8WithoutBom
{
    public static readonly Encoding Instance = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
