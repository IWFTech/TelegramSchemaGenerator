using System.Diagnostics;
using System.Text.Json;
using Xunit;
using IoFile = System.IO.File;

namespace TeleFlow.Telegram.SchemaGenerator.Tests;

public sealed class TelegramSchemaGeneratorCliTests
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();
    private static readonly string RichMessageMediaFixturePath = Path.Combine(
        RepositoryRoot,
        "tests",
        "TeleFlow.Telegram.SchemaGenerator.Tests",
        "Fixtures",
        "rich-message-media.raw.json");
    private static readonly string LiteralCardinalityFixturePath = Path.Combine(
        RepositoryRoot,
        "tests",
        "TeleFlow.Telegram.SchemaGenerator.Tests",
        "Fixtures",
        "literal-cardinality.raw.json");

    private const string RichMessageMediaUnionExpression =
        "InputMediaAnimation or InputMediaAudio or InputMediaPhoto or InputMediaVideo or InputMediaVoiceNote";

    private static readonly ConstantGroupExpectation[] ConstantGroupExpectations =
    [
        new("ButtonStyles", ["danger", "primary", "success"]),
        new("BotCommandScopeTypes", ["all_private_chats", "chat_member", "default"]),
        new("ChatMemberStatuses", ["administrator", "creator", "member"]),
        new("ChatTypes", ["channel", "group", "private", "sender", "supergroup"]),
        new("PassportElementErrorSources", ["data", "file"]),
        new("ReactionTypes", ["custom_emoji", "emoji", "paid"])
    ];

    private static readonly ConstantGroupEntryExpectation[] ConstantGroupEntryExpectations =
    [
        new(
            "InlineQueryResultTypes",
            [
                ("CachedPhoto", "photo"),
                ("Photo", "photo")
            ])
    ];

    private static readonly string[] MissingConstantGroupNames =
    [
        "ReactionTypeTypes"
    ];

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

            foreach (var expectation in ConstantGroupExpectations)
            {
                AssertConstantGroup(constantGroups, expectation.Name, expectation.TelegramValues);
            }

            foreach (var expectation in ConstantGroupEntryExpectations)
            {
                AssertConstantGroupEntries(constantGroups, expectation.Name, expectation.Values);
            }

            foreach (var name in MissingConstantGroupNames)
            {
                AssertNoConstantGroup(constantGroups, name);
            }
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void NormalizeAndGenerate_Commands_UseSemanticRichMessageMediaUnionName()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("teleflow-schema-generator-rich-message-media-");

        try
        {
            var normalizedOutputPath = Path.Combine(tempDirectory.FullName, "normalized.json");
            var generatedOutputPath = Path.Combine(tempDirectory.FullName, "Schema");

            RunGenerator("normalize", "--input", RichMessageMediaFixturePath, "--output", normalizedOutputPath);

            using var document = JsonDocument.Parse(IoFile.ReadAllText(normalizedOutputPath));
            var metadata = document.RootElement.GetProperty("Metadata");
            var abstractions = document.RootElement.GetProperty("Abstractions");
            var types = document.RootElement.GetProperty("Types");
            var union = abstractions
                .EnumerateArray()
                .Single(abstraction => abstraction.GetProperty("Name").GetString() == "InputRichMessageMediaItem");

            Assert.Equal(10, metadata.GetProperty("SchemaVersion").GetInt32());
            Assert.Equal(12, metadata.GetProperty("GeneratorVersion").GetInt32());
            Assert.Equal(RichMessageMediaUnionExpression, union.GetProperty("RawExpression").GetString());
            Assert.Equal("type-union", union.GetProperty("ValueShape").GetString());

            var unionCases = union.GetProperty("UnionCases").EnumerateArray().ToArray();
            Assert.Equal(
                ["InputMediaAnimation", "InputMediaAudio", "InputMediaPhoto", "InputMediaVideo", "InputMediaVoiceNote"],
                unionCases.Select(unionCase => unionCase.GetProperty("RawType").GetString()));
            Assert.All(
                unionCases,
                unionCase => Assert.Equal("property-discriminator", unionCase.GetProperty("MatchStrategy").GetString()));

            var richMessageMedia = types
                .EnumerateArray()
                .Single(type => type.GetProperty("Name").GetString() == "InputRichMessageMedia");
            var mediaProperty = richMessageMedia
                .GetProperty("Properties")
                .EnumerateArray()
                .Single(property => property.GetProperty("TelegramName").GetString() == "media");
            Assert.Equal("InputRichMessageMediaItem", mediaProperty.GetProperty("CSharpType").GetString());

            RunGenerator(
                "generate",
                "--input", normalizedOutputPath,
                "--generated-output", generatedOutputPath);

            var unionFile = IoFile.ReadAllText(
                Path.Combine(generatedOutputPath, "Abstractions", "InputRichMessageMediaItem.g.cs"));
            var richMessageMediaFile = IoFile.ReadAllText(
                Path.Combine(generatedOutputPath, "Types", "InputRichMessageMedia.g.cs"));

            Assert.Contains("public sealed partial record class InputRichMessageMediaItem", unionFile);
            Assert.Contains("public static implicit operator InputRichMessageMediaItem(InputMediaVoiceNote value)", unionFile);
            Assert.Contains("public required InputRichMessageMediaItem Media { get; init; } = null!;", richMessageMediaFile);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void NormalizeAndGenerate_Commands_OnlyInferSingularLiteralRequirements()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("teleflow-schema-generator-literal-cardinality-");

        try
        {
            var normalizedOutputPath = Path.Combine(tempDirectory.FullName, "normalized.json");
            var generatedOutputPath = Path.Combine(tempDirectory.FullName, "Schema");

            RunGenerator("normalize", "--input", LiteralCardinalityFixturePath, "--output", normalizedOutputPath);

            using var document = JsonDocument.Parse(IoFile.ReadAllText(normalizedOutputPath));
            var types = document.RootElement.GetProperty("Types");
            var listItemType = types
                .EnumerateArray()
                .Single(type => type.GetProperty("Name").GetString() == "RichBlockListItem");
            var listItemDiscriminator = listItemType
                .GetProperty("Properties")
                .EnumerateArray()
                .Single(property => property.GetProperty("TelegramName").GetString() == "type");
            var mediaType = types
                .EnumerateArray()
                .Single(type => type.GetProperty("Name").GetString() == "InputMediaPhoto");
            var mediaDiscriminator = mediaType
                .GetProperty("Properties")
                .EnumerateArray()
                .Single(property => property.GetProperty("TelegramName").GetString() == "type");

            Assert.Equal(JsonValueKind.Null, listItemDiscriminator.GetProperty("LiteralValue").ValueKind);
            Assert.Equal("photo", mediaDiscriminator.GetProperty("LiteralValue").GetString());

            RunGenerator(
                "generate",
                "--input", normalizedOutputPath,
                "--generated-output", generatedOutputPath);

            var listItemFile = IoFile.ReadAllText(
                Path.Combine(generatedOutputPath, "Types", "RichBlockListItem.g.cs"));
            var mediaFile = IoFile.ReadAllText(
                Path.Combine(generatedOutputPath, "Types", "InputMediaPhoto.g.cs"));

            Assert.DoesNotContain("IJsonOnDeserialized", listItemFile, StringComparison.Ordinal);
            Assert.DoesNotContain("TypeValue", listItemFile, StringComparison.Ordinal);
            Assert.Contains("TypeValue = \"photo\"", mediaFile, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Normalize_Command_ReportsOpaqueUnionExpressionAndNamingRemedy()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("teleflow-schema-generator-opaque-union-");

        try
        {
            const string opaqueUnionExpression =
                "InputMediaAnimation or InputMediaAudio or InputMediaDocument or InputMediaPhoto or InputMediaVideo or InputMediaVoiceNote";
            var rawOutputPath = Path.Combine(tempDirectory.FullName, "raw.json");
            var normalizedOutputPath = Path.Combine(tempDirectory.FullName, "normalized.json");
            var fixtureContents = IoFile.ReadAllText(RichMessageMediaFixturePath);

            IoFile.WriteAllText(
                rawOutputPath,
                fixtureContents.Replace(RichMessageMediaUnionExpression, opaqueUnionExpression, StringComparison.Ordinal));

            var exception = Assert.Throws<InvalidOperationException>(
                () => RunGenerator("normalize", "--input", rawOutputPath, "--output", normalizedOutputPath));

            Assert.Contains("prohibited opaque public union names", exception.Message, StringComparison.Ordinal);
            Assert.Contains(opaqueUnionExpression, exception.Message, StringComparison.Ordinal);
            Assert.Contains("TelegramUnionNamingRegistry", exception.Message, StringComparison.Ordinal);
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
            AssertUsesLfLineEndings(manifestPath);

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
            Assert.Equal(9, pipeline.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(12, pipeline.GetProperty("generatorVersion").GetInt32());

            var updateFile = IoFile.ReadAllText(Path.Combine(generatedOutputPath, "Types", "Update.g.cs"));
            AssertUsesLfLineEndings(Path.Combine(generatedOutputPath, "Types", "Update.g.cs"));
            Assert.Contains("//   Telegram Bot API version: 10.1", updateFile);
            Assert.Contains("//   Telegram Bot API changelog: https://core.telegram.org/bots/api-changelog#june-11-2026", updateFile);
            Assert.DoesNotContain("//   Source snapshot:", updateFile);
            Assert.DoesNotContain("//   Source SHA-256:", updateFile);
            Assert.DoesNotContain("//   Schema version:", updateFile);
            Assert.DoesNotContain("//   Generator version:", updateFile);

            var responseFile = IoFile.ReadAllText(Path.Combine(generatedOutputPath, "Responses", "TelegramApiResponse.g.cs"));
            Assert.Contains("public TResult? Result { get; init; }", responseFile, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "[JsonPropertyName(\"result\")]\n    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]",
                responseFile,
                StringComparison.Ordinal);
            Assert.Contains(
                "[JsonPropertyName(\"description\")]\n    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]",
                responseFile,
                StringComparison.Ordinal);

            var clientMethodFile = IoFile.ReadAllText(Path.Combine(telegramOutputPath, "Generated", "Methods", "SendMessageExtensions.g.cs"));
            AssertUsesLfLineEndings(Path.Combine(telegramOutputPath, "Generated", "Methods", "SendMessageExtensions.g.cs"));
            Assert.Contains("//   Kind: ClientMethod", clientMethodFile);
            Assert.DoesNotContain("//   Source snapshot:", clientMethodFile);
            Assert.DoesNotContain("//   Source SHA-256:", clientMethodFile);
            Assert.DoesNotContain("//   Schema version:", clientMethodFile);
            Assert.DoesNotContain("//   Generator version:", clientMethodFile);

            var constantsFile = IoFile.ReadAllText(Path.Combine(generatedOutputPath, "Constants", "ButtonStyles.g.cs"));
            AssertUsesLfLineEndings(Path.Combine(generatedOutputPath, "Constants", "ButtonStyles.g.cs"));
            Assert.Contains("namespace TeleFlow.Telegram.Schema.Constants;", constantsFile);
            Assert.Contains("public static class ButtonStyles", constantsFile);
            Assert.Contains("/// Telegram Bot API value <c>danger</c>.", constantsFile);
            Assert.Contains("public const string Danger = \"danger\";", constantsFile);
            Assert.Contains("public const string Primary = \"primary\";", constantsFile);
            Assert.Contains("public const string Success = \"success\";", constantsFile);

            var chatMemberStatusesFile = IoFile.ReadAllText(Path.Combine(generatedOutputPath, "Constants", "ChatMemberStatuses.g.cs"));
            Assert.Contains("public static class ChatMemberStatuses", chatMemberStatusesFile);
            Assert.Contains("public const string Administrator = \"administrator\";", chatMemberStatusesFile);
            Assert.Contains("public const string Creator = \"creator\";", chatMemberStatusesFile);
            Assert.Contains("public const string Member = \"member\";", chatMemberStatusesFile);
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

    private static void AssertConstantGroupEntries(
        JsonElement constantGroups,
        string name,
        (string Name, string TelegramValue)[] expectedValues)
    {
        var group = constantGroups
            .EnumerateArray()
            .First(item => item.GetProperty("Name").GetString() == name);
        var values = group.GetProperty("Values")
            .EnumerateArray()
            .Select(item => (
                Name: item.GetProperty("Name").GetString()!,
                TelegramValue: item.GetProperty("TelegramValue").GetString()!))
            .ToArray();

        Assert.Equal(expectedValues, values);
    }

    private static void AssertNoConstantGroup(JsonElement constantGroups, string name)
    {
        Assert.DoesNotContain(
            constantGroups.EnumerateArray(),
            item => item.GetProperty("Name").GetString() == name);
    }

    private static void AssertUsesLfLineEndings(string path)
    {
        var contents = IoFile.ReadAllText(path);

        Assert.Contains("\n", contents);
        Assert.DoesNotContain("\r\n", contents);
        Assert.DoesNotContain('\r', contents);
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
            "SchemaVersion": 9,
            "GeneratorVersion": 12
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
            },
            {
              "Name": "ChatMemberStatuses",
              "Summary": "Known Telegram Bot API ChatMember status values.",
              "Sources": [
                {
                  "TypeName": "ChatMemberAdministrator",
                  "TelegramName": "status"
                },
                {
                  "TypeName": "ChatMemberMember",
                  "TelegramName": "status"
                },
                {
                  "TypeName": "ChatMemberOwner",
                  "TelegramName": "status"
                }
              ],
              "Values": [
                {
                  "Name": "Administrator",
                  "TelegramValue": "administrator"
                },
                {
                  "Name": "Creator",
                  "TelegramValue": "creator"
                },
                {
                  "Name": "Member",
                  "TelegramValue": "member"
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

        startInfo.ArgumentList.Add(Path.Combine(
            RepositoryRoot,
            "src",
            "TeleFlow.Telegram.SchemaGenerator",
            "bin",
            "Release",
            "net10.0",
            "TeleFlow.Telegram.SchemaGenerator.dll"));
        startInfo.ArgumentList.Add(command);

        foreach (var argument in extraArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The schema generator process could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
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

    private sealed record ConstantGroupExpectation(
        string Name,
        string[] TelegramValues);

    private sealed record ConstantGroupEntryExpectation(
        string Name,
        (string Name, string TelegramValue)[] Values);
}
