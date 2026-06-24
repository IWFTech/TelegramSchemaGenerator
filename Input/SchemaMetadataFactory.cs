using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Input;

internal static class SchemaMetadataFactory
{
    private static readonly Regex H4SectionRegex = new(
        "<h4\\b(?<attributes>[^>]*)>(?<heading>.*?)</h4>(?<body>.*?)(?=<h4\\b|<h3\\b|</body>|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex AnchorRegex = new(
        "\\b(?:id|name)\\s*=\\s*[\"'](?<anchor>[^\"']+)[\"']",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BotApiVersionRegex = new(
        "\\bBot\\s+API\\s+(?<version>\\d+(?:\\.\\d+)*)\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlTagRegex = new(
        "<.*?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    public static TelegramSchemaMetadata CreateRaw(string sourceUrl, string html)
    {
        var botApiMetadata = ExtractTelegramBotApiMetadata(html);

        return new TelegramSchemaMetadata(
            sourceUrl,
            DateTimeOffset.UtcNow,
            ComputeSha256(html),
            botApiMetadata.Version,
            botApiMetadata.ReleasedAt,
            botApiMetadata.ChangelogAnchor,
            null,
            null);
    }

    public static TelegramSchemaMetadata CreateNormalized(TelegramSchemaMetadata rawMetadata)
    {
        return rawMetadata with
        {
            SchemaVersion = SchemaPipelineVersions.SchemaVersion,
            GeneratorVersion = SchemaPipelineVersions.GeneratorVersion
        };
    }

    private static (string Version, string ReleasedAt, string ChangelogAnchor) ExtractTelegramBotApiMetadata(string html)
    {
        foreach (Match section in H4SectionRegex.Matches(html))
        {
            var versionMatch = BotApiVersionRegex.Match(section.Groups["body"].Value);

            if (!versionMatch.Success)
            {
                continue;
            }

            var anchor = ExtractAnchor(section);
            var releasedAt = ParseReleaseDate(ExtractHeadingText(section.Groups["heading"].Value), anchor);

            return (
                versionMatch.Groups["version"].Value,
                releasedAt,
                anchor);
        }

        throw new InvalidOperationException(
            "Could not extract Telegram Bot API version metadata from source HTML. Expected a recent-changes h4 section containing 'Bot API <version>'.");
    }

    private static string ExtractAnchor(Match section)
    {
        var anchorMatch = AnchorRegex.Match(section.Groups["attributes"].Value);

        if (!anchorMatch.Success)
        {
            anchorMatch = AnchorRegex.Match(section.Groups["heading"].Value);
        }

        if (!anchorMatch.Success || string.IsNullOrWhiteSpace(anchorMatch.Groups["anchor"].Value))
        {
            throw new InvalidOperationException("Could not extract Telegram Bot API changelog anchor from the latest Bot API section.");
        }

        return anchorMatch.Groups["anchor"].Value;
    }

    private static string ExtractHeadingText(string html)
    {
        return WebUtility.HtmlDecode(HtmlTagRegex.Replace(html, string.Empty)).Trim();
    }

    private static string ParseReleaseDate(string value, string anchor)
    {
        if (!DateTime.TryParseExact(
                value,
                ["MMMM d, yyyy", "MMMM dd, yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            throw new InvalidOperationException(
                $"Could not parse Telegram Bot API release date '{value}' from changelog section '{anchor}'.");
        }

        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return string.Create(bytes.Length * 2, bytes, static (chars, source) =>
        {
            const string LowerHex = "0123456789abcdef";

            for (var index = 0; index < source.Length; index++)
            {
                var value = source[index];
                chars[index * 2] = LowerHex[value >> 4];
                chars[(index * 2) + 1] = LowerHex[value & 0xF];
            }
        });
    }
}
