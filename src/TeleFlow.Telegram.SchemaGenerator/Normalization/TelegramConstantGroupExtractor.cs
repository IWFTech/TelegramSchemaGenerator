using System.Text.RegularExpressions;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Normalization;

internal static class TelegramConstantGroupExtractor
{
    private static readonly ConstantGroupSpec[] Specs =
    [
        new(
            "ButtonStyles",
            "Known Telegram Bot API button style values.",
            new Regex("[\"“'](?<value>[a-zA-Z0-9_:-]+)[\"”']", RegexOptions.Compiled),
            [
                new("InlineKeyboardButton", "style"),
                new("KeyboardButton", "style")
            ],
            []),
        new(
            "ChatTypes",
            "Known Telegram Bot API chat type values.",
            new Regex("[\"“'](?<value>[a-zA-Z0-9_:-]+)[\"”']", RegexOptions.Compiled),
            [
                new("Chat", "type")
            ],
            [
                new("sender")
            ]),
        new(
            "ReactionTypes",
            "Known Telegram Bot API reaction type values.",
            new Regex("[\"“'](?<value>[a-zA-Z0-9_:-]+)[\"”']", RegexOptions.Compiled),
            [
                new("ReactionTypeEmoji", "type"),
                new("ReactionTypeCustomEmoji", "type"),
                new("ReactionTypePaid", "type")
            ],
            [])
    ];

    public static IReadOnlyList<NormalizedTelegramConstantGroup> Extract(IReadOnlyList<NormalizedTelegramType> types)
    {
        var typesByName = types.ToDictionary(static type => type.Name, StringComparer.Ordinal);
        var groups = new List<NormalizedTelegramConstantGroup>();

        foreach (var spec in Specs)
        {
            var values = new SortedSet<string>(StringComparer.Ordinal);
            var sources = new List<NormalizedTelegramConstantSource>();
            var extractedValueCount = 0;

            foreach (var source in spec.Sources)
            {
                if (!typesByName.TryGetValue(source.TypeName, out var type))
                {
                    continue;
                }

                var property = type.Properties.FirstOrDefault(property =>
                    property.TelegramName.Equals(source.TelegramName, StringComparison.Ordinal));
                if (property is null)
                {
                    continue;
                }

                sources.Add(new NormalizedTelegramConstantSource(source.TypeName, source.TelegramName));

                if (!string.IsNullOrWhiteSpace(property.LiteralValue))
                {
                    values.Add(property.LiteralValue);
                    extractedValueCount++;
                }

                foreach (Match match in spec.ValueRegex.Matches(property.Summary))
                {
                    values.Add(match.Groups["value"].Value);
                    extractedValueCount++;
                }
            }

            if (sources.Count == 0)
            {
                continue;
            }

            if (extractedValueCount == 0)
            {
                throw new InvalidOperationException(
                    $"The constant group '{spec.Name}' matched schema sources but did not extract any source values.");
            }

            foreach (var value in spec.StaticValues)
            {
                values.Add(value.Value);
            }

            groups.Add(new NormalizedTelegramConstantGroup(
                spec.Name,
                spec.Summary,
                sources
                    .OrderBy(static source => source.TypeName, StringComparer.Ordinal)
                    .ThenBy(static source => source.TelegramName, StringComparer.Ordinal)
                    .ToArray(),
                values
                    .Select(value => new NormalizedTelegramConstantValue(ToPascalCase(value), value))
                    .ToArray()));
        }

        return groups
            .OrderBy(static group => group.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ToPascalCase(string value)
    {
        var parts = value.Split(['_', '-', ' ', ':'], StringSplitOptions.RemoveEmptyEntries);
        var name = string.Concat(parts.Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"The Telegram constant value '{value}' cannot be converted to a C# identifier.");
        }

        return char.IsDigit(name[0]) ? "Value" + name : name;
    }

    private sealed record ConstantGroupSpec(
        string Name,
        string Summary,
        Regex ValueRegex,
        IReadOnlyList<ConstantSourceSpec> Sources,
        IReadOnlyList<ConstantValueSpec> StaticValues);

    private sealed record ConstantSourceSpec(
        string TypeName,
        string TelegramName);

    private sealed record ConstantValueSpec(string Value);
}
