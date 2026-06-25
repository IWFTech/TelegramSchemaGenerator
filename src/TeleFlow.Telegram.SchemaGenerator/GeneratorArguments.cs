namespace TeleFlow.Telegram.SchemaGenerator;

internal sealed record GeneratorArguments(
    GeneratorCommand Command,
    string? InputPath,
    string? OutputPath,
    string? RawOutputPath,
    string? NormalizedOutputPath,
    string? GeneratedOutputPath,
    string? TelegramOutputPath,
    string? InputHtmlPath,
    string? SourceUrl)
{
    public const string DefaultSourceUrl = "https://core.telegram.org/bots/api";

    public static GeneratorArguments Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new InvalidOperationException("A command is required: parse-docs, normalize, generate, or all.");
        }

        var command = args[0] switch
        {
            "parse-docs" => GeneratorCommand.ParseDocs,
            "normalize" => GeneratorCommand.Normalize,
            "generate" => GeneratorCommand.Generate,
            "all" => GeneratorCommand.All,
            _ => throw new InvalidOperationException($"Unknown command '{args[0]}'.")
        };

        string? inputPath = null;
        string? outputPath = null;
        string? rawOutputPath = null;
        string? normalizedOutputPath = null;
        string? generatedOutputPath = null;
        string? telegramOutputPath = null;
        string? inputHtmlPath = null;
        string? sourceUrl = null;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--input":
                    inputPath = GetValue(args, ++index, "--input");
                    break;
                case "--output":
                    outputPath = GetValue(args, ++index, "--output");
                    break;
                case "--raw-output":
                    rawOutputPath = GetValue(args, ++index, "--raw-output");
                    break;
                case "--normalized-output":
                    normalizedOutputPath = GetValue(args, ++index, "--normalized-output");
                    break;
                case "--generated-output":
                    generatedOutputPath = GetValue(args, ++index, "--generated-output");
                    break;
                case "--telegram-output":
                    telegramOutputPath = GetValue(args, ++index, "--telegram-output");
                    break;
                case "--input-html":
                    inputHtmlPath = GetValue(args, ++index, "--input-html");
                    break;
                case "--url":
                    sourceUrl = GetValue(args, ++index, "--url");
                    break;
                default:
                    throw new InvalidOperationException($"Unknown argument '{args[index]}'.");
            }
        }

        return new GeneratorArguments(
            command,
            ResolvePath(inputPath),
            ResolvePath(outputPath),
            ResolvePath(rawOutputPath),
            ResolvePath(normalizedOutputPath),
            ResolvePath(generatedOutputPath),
            ResolvePath(telegramOutputPath),
            ResolvePath(inputHtmlPath),
            sourceUrl);
    }

    public string GetRequiredInputPath(string stage)
    {
        return stage switch
        {
            "raw" => InputPath ?? throw new InvalidOperationException("An --input path is required for normalize."),
            "normalized" => InputPath ?? throw new InvalidOperationException("An --input path is required for generate."),
            _ => throw new InvalidOperationException($"Unknown input stage '{stage}'.")
        };
    }

    public string GetRequiredOutputPath(string stage)
    {
        return stage switch
        {
            "raw" => RawOutputPath ?? OutputPath ?? throw new InvalidOperationException("A raw output path is required."),
            "normalized" => NormalizedOutputPath ?? OutputPath ?? throw new InvalidOperationException("A normalized output path is required."),
            "generated" => GeneratedOutputPath ?? OutputPath ?? throw new InvalidOperationException("A generated output path is required."),
            "telegram" => TelegramOutputPath ?? throw new InvalidOperationException("A Telegram runtime output path is required."),
            _ => throw new InvalidOperationException($"Unknown output stage '{stage}'.")
        };
    }

    public string? GetOptionalOutputPath(string stage)
    {
        return stage switch
        {
            "telegram" => TelegramOutputPath,
            _ => throw new InvalidOperationException($"Unknown output stage '{stage}'.")
        };
    }

    private static string GetValue(string[] args, int index, string argumentName)
    {
        if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new InvalidOperationException($"A value must be provided for {argumentName}.");
        }

        return args[index];
    }

    private static string? ResolvePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
    }
}

internal enum GeneratorCommand
{
    ParseDocs,
    Normalize,
    Generate,
    All
}
