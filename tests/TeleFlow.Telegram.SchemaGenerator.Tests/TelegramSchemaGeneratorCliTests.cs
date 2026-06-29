using System.Diagnostics;
using System.Text.Json;
using Xunit;
using IoFile = System.IO.File;

namespace TeleFlow.Telegram.SchemaGenerator.Tests;

public sealed class TelegramSchemaGeneratorCliTests
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();

    [Fact]
    public void ParseDocs_Command_ParsesRepresentativeHtmlFixture()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("teleflow-schema-generator-parse-");

        try
        {
            var outputPath = Path.Combine(tempDirectory.FullName, "raw.json");
            RunGenerator(
                "parse-docs",
                "--input-html", Path.Combine(RepositoryRoot, "tests", "TeleFlow.Telegram.SchemaGenerator.Tests", "Fixtures", "telegram-doc-sample.html"),
                "--output", outputPath);

            using var document = JsonDocument.Parse(IoFile.ReadAllText(outputPath));
            var metadata = document.RootElement.GetProperty("Metadata");
            var categories = document.RootElement.GetProperty("Categories");

            Assert.Equal("https://core.telegram.org/bots/api", metadata.GetProperty("SourceUrl").GetString());
            Assert.False(string.IsNullOrWhiteSpace(metadata.GetProperty("SourceCapturedAtUtc").GetString()));
            Assert.Matches("^[0-9a-f]{64}$", metadata.GetProperty("SourceSha256").GetString()!);
            AssertBotApiMetadata(metadata);
            Assert.True(categories.GetArrayLength() >= 4);
            Assert.Contains(categories.EnumerateArray(), category => category.GetProperty("Anchor").GetString() == "recent-changes");
            Assert.Contains(categories.EnumerateArray(), category => category.GetProperty("Anchor").GetString() == "available-methods");

            var availableMethods = categories.EnumerateArray().First(category => category.GetProperty("Anchor").GetString() == "available-methods");
            var sections = availableMethods.GetProperty("Sections");

            Assert.Contains(sections.EnumerateArray(), section => section.GetProperty("Title").GetString() == "getMe");
            Assert.Contains(sections.EnumerateArray(), section => section.GetProperty("Title").GetString() == "getMyName");
            Assert.Contains(sections.EnumerateArray(), section => section.GetProperty("Classification").GetString() == "method");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void SourceSha256_IsDeterministicForSameHtmlInput()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("teleflow-schema-generator-sha-");

        try
        {
            var fixturePath = Path.Combine(RepositoryRoot, "tests", "TeleFlow.Telegram.SchemaGenerator.Tests", "Fixtures", "telegram-doc-sample.html");
            var firstOutputPath = Path.Combine(tempDirectory.FullName, "raw-1.json");
            var secondOutputPath = Path.Combine(tempDirectory.FullName, "raw-2.json");

            RunGenerator("parse-docs", "--input-html", fixturePath, "--output", firstOutputPath);
            RunGenerator("parse-docs", "--input-html", fixturePath, "--output", secondOutputPath);

            using var firstDocument = JsonDocument.Parse(IoFile.ReadAllText(firstOutputPath));
            using var secondDocument = JsonDocument.Parse(IoFile.ReadAllText(secondOutputPath));

            Assert.Equal(
                firstDocument.RootElement.GetProperty("Metadata").GetProperty("SourceSha256").GetString(),
                secondDocument.RootElement.GetProperty("Metadata").GetProperty("SourceSha256").GetString());
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Normalize_Command_ExtractsEnumLikeConstants()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("teleflow-schema-generator-constants-");

        try
        {
            var rawOutputPath = Path.Combine(tempDirectory.FullName, "raw.json");
            var normalizedOutputPath = Path.Combine(tempDirectory.FullName, "normalized.json");

            RunGenerator(
                "parse-docs",
                "--input-html", Path.Combine(RepositoryRoot, "tests", "TeleFlow.Telegram.SchemaGenerator.Tests", "Fixtures", "telegram-doc-sample.html"),
                "--output", rawOutputPath);
            RunGenerator("normalize", "--input", rawOutputPath, "--output", normalizedOutputPath);

            using var document = JsonDocument.Parse(IoFile.ReadAllText(normalizedOutputPath));
            var constantGroups = document.RootElement.GetProperty("ConstantGroups");

            AssertConstantGroup(
                constantGroups,
                "ButtonStyles",
                ["danger", "primary", "success"]);
            AssertConstantGroup(
                constantGroups,
                "ChatTypes",
                ["channel", "group", "private", "sender", "supergroup"]);
            AssertConstantGroup(
                constantGroups,
                "ReactionTypes",
                ["custom_emoji", "emoji", "paid"]);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Generate_Command_WritesGeneratedManifestAndStableHeaders()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("teleflow-schema-generator-generate-");

        try
        {
            var normalizedOutputPath = Path.Combine(tempDirectory.FullName, "normalized.json");
            var generatedOutputPath = Path.Combine(tempDirectory.FullName, "Schema");
            var telegramOutputPath = Path.Combine(tempDirectory.FullName, "Telegram");
            IoFile.WriteAllText(normalizedOutputPath, MinimalNormalizedSnapshotJson);

            RunGenerator(
                "generate",
                "--input", normalizedOutputPath,
                "--generated-output", generatedOutputPath,
                "--telegram-output", telegramOutputPath);

            var manifestPath = Path.Combine(generatedOutputPath, "telegram-bot-api.manifest.json");
            Assert.True(IoFile.Exists(manifestPath));

            using var manifestDocument = JsonDocument.Parse(IoFile.ReadAllText(manifestPath));
            var manifest = manifestDocument.RootElement;
            var source = manifest.GetProperty("source");
            var telegramBotApi = manifest.GetProperty("telegramBotApi");
            var pipeline = manifest.GetProperty("pipeline");

            Assert.Equal(1, manifest.GetProperty("manifestVersion").GetInt32());
            Assert.Equal("https://core.telegram.org/bots/api", source.GetProperty("url").GetString());
            Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("capturedAtUtc").GetString()));
            Assert.Matches("^[0-9a-f]{64}$", source.GetProperty("sha256").GetString()!);
            Assert.Equal("10.1", telegramBotApi.GetProperty("version").GetString());
            Assert.Equal("2026-06-11", telegramBotApi.GetProperty("releasedAt").GetString());
            Assert.Equal("june-11-2026", telegramBotApi.GetProperty("changelogAnchor").GetString());
            Assert.Equal("https://core.telegram.org/bots/api-changelog#june-11-2026", telegramBotApi.GetProperty("changelogUrl").GetString());
            Assert.Equal(7, pipeline.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(10, pipeline.GetProperty("generatorVersion").GetInt32());

            var updateFile = IoFile.ReadAllText(Path.Combine(generatedOutputPath, "Types", "Update.g.cs"));
            Assert.Contains("//   Telegram Bot API version: 10.1", updateFile);
            Assert.Contains("//   Telegram Bot API changelog: https://core.telegram.org/bots/api-changelog#june-11-2026", updateFile);
            Assert.DoesNotContain("//   Source snapshot:", updateFile);
            Assert.DoesNotContain("//   Source SHA-256:", updateFile);
            Assert.DoesNotContain("//   Schema version:", updateFile);
            Assert.DoesNotContain("//   Generator version:", updateFile);

            var clientMethodFile = IoFile.ReadAllText(Path.Combine(telegramOutputPath, "Generated", "Methods", "SendMessageExtensions.g.cs"));
            Assert.Contains("//   Kind: ClientMethod", clientMethodFile);
            Assert.DoesNotContain("//   Source snapshot:", clientMethodFile);
            Assert.DoesNotContain("//   Source SHA-256:", clientMethodFile);
            Assert.DoesNotContain("//   Schema version:", clientMethodFile);
            Assert.DoesNotContain("//   Generator version:", clientMethodFile);

            var constantsFile = IoFile.ReadAllText(Path.Combine(generatedOutputPath, "Constants", "ButtonStyles.g.cs"));
            Assert.Contains("namespace TeleFlow.Telegram.Schema.Constants;", constantsFile);
            Assert.Contains("public static class ButtonStyles", constantsFile);
            Assert.Contains("/// Telegram Bot API value <c>danger</c>.", constantsFile);
            Assert.Contains("public const string Danger = \"danger\";", constantsFile);
            Assert.Contains("public const string Primary = \"primary\";", constantsFile);
            Assert.Contains("public const string Success = \"success\";", constantsFile);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static void AssertBotApiMetadata(JsonElement metadata)
    {
        Assert.Equal("10.1", metadata.GetProperty("TelegramBotApiVersion").GetString());
        Assert.Equal("2026-06-11", metadata.GetProperty("TelegramBotApiReleasedAt").GetString());
        Assert.Equal("june-11-2026", metadata.GetProperty("TelegramBotApiChangelogAnchor").GetString());
    }

    private static void AssertConstantGroup(
        JsonElement constantGroups,
        string name,
        string[] expectedTelegramValues)
    {
        var group = constantGroups
            .EnumerateArray()
            .First(item => item.GetProperty("Name").GetString() == name);
        var values = group.GetProperty("Values")
            .EnumerateArray()
            .Select(item => item.GetProperty("TelegramValue").GetString())
            .ToArray();

        Assert.Equal(expectedTelegramValues, values);
    }

    private const string MinimalNormalizedSnapshotJson =
        """
        {
          "Metadata": {
            "SourceUrl": "https://core.telegram.org/bots/api",
            "SourceCapturedAtUtc": "2026-06-18T14:34:01.3212475+00:00",
            "SourceSha256": "8d628bd894ffd733d2978983a94cc4d3eaa3877e30593554647e4849e612fe8d",
            "TelegramBotApiVersion": "10.1",
            "TelegramBotApiReleasedAt": "2026-06-11",
            "TelegramBotApiChangelogAnchor": "june-11-2026",
            "SchemaVersion": 7,
            "GeneratorVersion": 10
          },
          "Types": [
            {
              "Name": "Update",
              "Anchor": "update",
              "Summary": "This object represents an incoming update.",
              "Remarks": [],
              "Kind": "object",
              "IsAliasLike": false,
              "UnionMembers": [],
              "UnionCases": [],
              "NamedUnionStrategy": null,
              "NamedUnionDiscriminatorProperty": null,
              "Properties": [
                {
                  "Name": "UpdateId",
                  "TelegramName": "update_id",
                  "RawType": "Integer",
                  "TypeExpression": { "Kind": "scalar", "Text": "Integer", "Members": [] },
                  "CSharpType": "long",
                  "Required": true,
                  "LiteralValue": null,
                  "Summary": "The update's unique identifier."
                },
                {
                  "Name": "Message",
                  "TelegramName": "message",
                  "RawType": "Message",
                  "TypeExpression": { "Kind": "type", "Text": "Message", "Members": [] },
                  "CSharpType": "Message?",
                  "Required": false,
                  "LiteralValue": null,
                  "Summary": "New incoming message."
                }
              ]
            },
            {
              "Name": "Message",
              "Anchor": "message",
              "Summary": "This object represents a message.",
              "Remarks": [],
              "Kind": "object",
              "IsAliasLike": false,
              "UnionMembers": [],
              "UnionCases": [],
              "NamedUnionStrategy": null,
              "NamedUnionDiscriminatorProperty": null,
              "Properties": [
                {
                  "Name": "MessageId",
                  "TelegramName": "message_id",
                  "RawType": "Integer",
                  "TypeExpression": { "Kind": "scalar", "Text": "Integer", "Members": [] },
                  "CSharpType": "long",
                  "Required": true,
                  "LiteralValue": null,
                  "Summary": "Unique message identifier."
                },
                {
                  "Name": "Text",
                  "TelegramName": "text",
                  "RawType": "String",
                  "TypeExpression": { "Kind": "scalar", "Text": "String", "Members": [] },
                  "CSharpType": "string?",
                  "Required": false,
                  "LiteralValue": null,
                  "Summary": "For text messages, the actual UTF-8 text."
                }
              ]
            }
          ],
          "Methods": [
            {
              "Name": "SendMessage",
              "Anchor": "sendmessage",
              "TelegramMethodName": "sendMessage",
              "Summary": "Use this method to send text messages.",
              "Remarks": [],
              "RawResultType": "Message",
              "ResultExpression": { "Kind": "type", "Text": "Message", "Members": [] },
              "ResultType": "Message",
              "Parameters": [
                {
                  "Name": "ChatId",
                  "TelegramName": "chat_id",
                  "RawType": "Integer",
                  "TypeExpression": { "Kind": "scalar", "Text": "Integer", "Members": [] },
                  "CSharpType": "long",
                  "Required": true,
                  "LiteralValue": null,
                  "Summary": "Unique identifier for the target chat."
                },
                {
                  "Name": "Text",
                  "TelegramName": "text",
                  "RawType": "String",
                  "TypeExpression": { "Kind": "scalar", "Text": "String", "Members": [] },
                  "CSharpType": "string",
                  "Required": true,
                  "LiteralValue": null,
                  "Summary": "Text of the message to be sent."
                }
              ]
            }
          ],
          "Abstractions": [],
          "ConstantGroups": [
            {
              "Name": "ButtonStyles",
              "Summary": "Known Telegram Bot API button style values.",
              "Sources": [
                {
                  "TypeName": "InlineKeyboardButton",
                  "TelegramName": "style"
                }
              ],
              "Values": [
                {
                  "Name": "Danger",
                  "TelegramValue": "danger"
                },
                {
                  "Name": "Primary",
                  "TelegramValue": "primary"
                },
                {
                  "Name": "Success",
                  "TelegramValue": "success"
                }
              ]
            }
          ]
        }
        """;

    private static void RunGenerator(string command, params string[] extraArguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepositoryRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(RepositoryRoot, "src", "TeleFlow.Telegram.SchemaGenerator", "TeleFlow.Telegram.SchemaGenerator.csproj"));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(command);

        foreach (var argument in extraArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The schema generator process could not be started.");

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"Schema generator failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !IoFile.Exists(Path.Combine(directory.FullName, "TeleFlow.Telegram.SchemaGenerator.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be resolved.");
    }
}
