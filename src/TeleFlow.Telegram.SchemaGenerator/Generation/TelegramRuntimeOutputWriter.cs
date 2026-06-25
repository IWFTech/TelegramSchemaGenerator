using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class TelegramRuntimeOutputWriter
{
    public static void Write(string outputDirectory, NormalizedTelegramSchema schema)
    {
        Validate(schema);

        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);

        var methodsDirectory = Path.Combine(root, "Generated", "Methods");
        RecreateDirectory(methodsDirectory);

        var abstractionNames = schema.Abstractions
            .Select(static abstraction => abstraction.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var method in schema.Methods.OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            File.WriteAllText(
                Path.Combine(methodsDirectory, method.Name + "Extensions.g.cs"),
                TelegramClientMethodExtensionsGenerator.Generate(schema, method, abstractionNames),
                Utf8WithoutBom.Instance);
        }

        File.WriteAllText(
            Path.Combine(root, "Generated", "TelegramUpdateTypes.g.cs"),
            TelegramUpdateTypesGenerator.Generate(schema),
            Utf8WithoutBom.Instance);
    }

    private static void Validate(NormalizedTelegramSchema schema)
    {
        var duplicateExtensionNames = schema.Methods
            .Select(static method => method.Name + "Async")
            .GroupBy(static name => name, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicateExtensionNames.Length > 0)
        {
            throw new InvalidOperationException(
                "Duplicate generated Telegram client extension method names were detected: " +
                string.Join(", ", duplicateExtensionNames));
        }

    }

    private static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }
}
