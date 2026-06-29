using System.Text.Json;
using System.Text.Json.Serialization;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class TelegramBotApiManifestWriter
{
    public const string FileName = "telegram-bot-api.manifest.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static void Write(string outputDirectory, TelegramSchemaMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var manifest = new TelegramBotApiManifest(
            ManifestVersion: 1,
            Source: new TelegramBotApiSourceManifest(
                Url: metadata.SourceUrl,
                CapturedAtUtc: metadata.SourceCapturedAtUtc.ToUniversalTime(),
                Sha256: metadata.SourceSha256),
            TelegramBotApi: new TelegramBotApiVersionManifest(
                Version: Require(metadata.TelegramBotApiVersion, nameof(metadata.TelegramBotApiVersion)),
                ReleasedAt: Require(metadata.TelegramBotApiReleasedAt, nameof(metadata.TelegramBotApiReleasedAt)),
                ChangelogAnchor: Require(metadata.TelegramBotApiChangelogAnchor, nameof(metadata.TelegramBotApiChangelogAnchor)),
                ChangelogUrl: $"https://core.telegram.org/bots/api-changelog#{Require(metadata.TelegramBotApiChangelogAnchor, nameof(metadata.TelegramBotApiChangelogAnchor))}"),
            Pipeline: new TelegramBotApiPipelineManifest(
                SchemaVersion: Require(metadata.SchemaVersion, nameof(metadata.SchemaVersion)),
                GeneratorVersion: Require(metadata.GeneratorVersion, nameof(metadata.GeneratorVersion))));

        var contents = JsonSerializer.Serialize(manifest, SerializerOptions) + "\n";
        GeneratedTextWriter.WriteAllText(Path.Combine(outputDirectory, FileName), contents);
    }

    private static string Require(string? value, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Generated manifest metadata value '{name}' is missing.")
            : value;
    }

    private static int Require(int? value, string name)
    {
        return value is null or <= 0
            ? throw new InvalidOperationException($"Generated manifest metadata value '{name}' is missing.")
            : value.Value;
    }

    private sealed record TelegramBotApiManifest(
        [property: JsonPropertyName("manifestVersion")] int ManifestVersion,
        [property: JsonPropertyName("source")] TelegramBotApiSourceManifest Source,
        [property: JsonPropertyName("telegramBotApi")] TelegramBotApiVersionManifest TelegramBotApi,
        [property: JsonPropertyName("pipeline")] TelegramBotApiPipelineManifest Pipeline);

    private sealed record TelegramBotApiSourceManifest(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("capturedAtUtc")] DateTimeOffset CapturedAtUtc,
        [property: JsonPropertyName("sha256")] string Sha256);

    private sealed record TelegramBotApiVersionManifest(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("releasedAt")] string ReleasedAt,
        [property: JsonPropertyName("changelogAnchor")] string ChangelogAnchor,
        [property: JsonPropertyName("changelogUrl")] string ChangelogUrl);

    private sealed record TelegramBotApiPipelineManifest(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("generatorVersion")] int GeneratorVersion);
}
