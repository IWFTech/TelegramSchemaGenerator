using TeleFlow.Telegram.SchemaGenerator;
using TeleFlow.Telegram.SchemaGenerator.Generation;
using TeleFlow.Telegram.SchemaGenerator.Input;
using TeleFlow.Telegram.SchemaGenerator.Normalization;
using TeleFlow.Telegram.SchemaGenerator.Parsing;
using TeleFlow.Telegram.SchemaGenerator.Validation;
using TeleFlow.Telegram.SchemaGenerator.Writers;

var arguments = GeneratorArguments.Parse(args);

return arguments.Command switch
{
    GeneratorCommand.ParseDocs => await RunParseDocsAsync(arguments).ConfigureAwait(false),
    GeneratorCommand.Normalize => RunNormalize(arguments),
    GeneratorCommand.Generate => RunGenerate(arguments),
    GeneratorCommand.All => await RunAllAsync(arguments).ConfigureAwait(false),
    _ => throw new InvalidOperationException($"Unsupported command '{arguments.Command}'.")
};

static async Task<int> RunParseDocsAsync(GeneratorArguments arguments)
{
    var html = await DocumentationLoader.LoadHtmlAsync(arguments, CancellationToken.None).ConfigureAwait(false);
    var metadata = SchemaMetadataFactory.CreateRaw(arguments.SourceUrl ?? GeneratorArguments.DefaultSourceUrl, html);
    var snapshot = TelegramDocumentationParser.Parse(html, metadata);
    TelegramSchemaValidator.Validate(snapshot);
    SnapshotWriter.Write(arguments.GetRequiredOutputPath("raw"), snapshot);
    return 0;
}

static int RunNormalize(GeneratorArguments arguments)
{
    var rawSnapshot = SnapshotReader.ReadRaw(arguments.GetRequiredInputPath("raw"));
    TelegramSchemaValidator.Validate(rawSnapshot);
    var normalized = TelegramSchemaNormalizer.Normalize(rawSnapshot);
    TelegramSchemaValidator.Validate(normalized);
    SnapshotWriter.Write(arguments.GetRequiredOutputPath("normalized"), normalized);
    return 0;
}

static int RunGenerate(GeneratorArguments arguments)
{
    var normalized = SnapshotReader.ReadNormalized(arguments.GetRequiredInputPath("normalized"));
    TelegramSchemaValidator.Validate(normalized);
    SchemaOutputWriter.Write(arguments.GetRequiredOutputPath("generated"), normalized);
    if (arguments.GetOptionalOutputPath("telegram") is { } telegramOutputPath)
    {
        TelegramRuntimeOutputWriter.Write(telegramOutputPath, normalized);
    }

    return 0;
}

static async Task<int> RunAllAsync(GeneratorArguments arguments)
{
    var html = await DocumentationLoader.LoadHtmlAsync(arguments, CancellationToken.None).ConfigureAwait(false);
    var metadata = SchemaMetadataFactory.CreateRaw(arguments.SourceUrl ?? GeneratorArguments.DefaultSourceUrl, html);
    var rawSnapshot = TelegramDocumentationParser.Parse(html, metadata);
    SnapshotWriter.Write(arguments.GetRequiredOutputPath("raw"), rawSnapshot);

    TelegramSchemaValidator.Validate(rawSnapshot);
    var normalized = TelegramSchemaNormalizer.Normalize(rawSnapshot);
    TelegramSchemaValidator.Validate(normalized);
    SnapshotWriter.Write(arguments.GetRequiredOutputPath("normalized"), normalized);

    SchemaOutputWriter.Write(arguments.GetRequiredOutputPath("generated"), normalized);
    if (arguments.GetOptionalOutputPath("telegram") is { } telegramOutputPath)
    {
        TelegramRuntimeOutputWriter.Write(telegramOutputPath, normalized);
    }

    return 0;
}
