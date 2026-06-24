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

    private static void AssertBotApiMetadata(JsonElement metadata)
    {
        Assert.Equal("10.1", metadata.GetProperty("TelegramBotApiVersion").GetString());
        Assert.Equal("2026-06-11", metadata.GetProperty("TelegramBotApiReleasedAt").GetString());
        Assert.Equal("june-11-2026", metadata.GetProperty("TelegramBotApiChangelogAnchor").GetString());
    }

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
        startInfo.ArgumentList.Add(Path.Combine(RepositoryRoot, "TeleFlow.Telegram.SchemaGenerator.csproj"));
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

        while (directory is not null && !IoFile.Exists(Path.Combine(directory.FullName, "TeleFlow.Telegram.SchemaGenerator.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be resolved.");
    }
}
