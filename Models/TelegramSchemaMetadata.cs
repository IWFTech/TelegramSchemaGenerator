namespace TeleFlow.Telegram.SchemaGenerator.Models;

internal sealed record TelegramSchemaMetadata(
    string SourceUrl,
    DateTimeOffset SourceCapturedAtUtc,
    string SourceSha256,
    string? TelegramBotApiVersion,
    string? TelegramBotApiReleasedAt,
    string? TelegramBotApiChangelogAnchor,
    int? SchemaVersion,
    int? GeneratorVersion);
