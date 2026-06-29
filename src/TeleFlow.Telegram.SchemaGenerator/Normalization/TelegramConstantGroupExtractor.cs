using System.Text.RegularExpressions;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Normalization;

internal static class TelegramConstantGroupExtractor
{
    private static readonly Regex QuotedTelegramValueRegex = new(
        "[\"“'](?<value>[a-zA-Z0-9_:-]+)[\"”']",
        RegexOptions.Compiled);

    private static readonly DiscriminatorNameRule[] DiscriminatorNameRules =
    [
        new("source", "Source", "Sources"),
        new("status", "Status", "Statuses"),
        new("style", "Style", "Styles"),
        new("type", "Type", "Types")
    ];

    private static readonly ConstantGroupSpec[] Specs =
    [
        new(
            "ButtonStyles",
            "Known Telegram Bot API button style values.",
            QuotedTelegramValueRegex,
            [
                new("InlineKeyboardButton", "style"),
                new("KeyboardButton", "style")
            ],
            []),
        new(
            "ChatTypes",
            "Known Telegram Bot API chat type values.",
            QuotedTelegramValueRegex,
            [
                new("Chat", "type")
            ],
            [
                new("sender")
            ]),
        new(
            "ReactionTypes",
            "Known Telegram Bot API reaction type values.",
            QuotedTelegramValueRegex,
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

        groups.AddRange(ExtractConfiguredGroups(typesByName));
        groups.AddRange(ExtractUnionDiscriminatorGroups(types));

        return MergeGroups(groups)
            .OrderBy(static group => group.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<NormalizedTelegramConstantGroup> ExtractConfiguredGroups(
        Dictionary<string, NormalizedTelegramType> typesByName)
    {
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

            yield return new NormalizedTelegramConstantGroup(
                spec.Name,
                spec.Summary,
                sources
                    .OrderBy(static source => source.TypeName, StringComparer.Ordinal)
                    .ThenBy(static source => source.TelegramName, StringComparer.Ordinal)
                    .ToArray(),
                values
                    .Select(value => new NormalizedTelegramConstantValue(ToPascalCase(value), value))
                    .ToArray());
        }
    }

    private static IEnumerable<NormalizedTelegramConstantGroup> ExtractUnionDiscriminatorGroups(
        IReadOnlyList<NormalizedTelegramType> types)
    {
        foreach (var type in types)
        {
            if (type.NamedUnionStrategy != "property-discriminator" ||
                string.IsNullOrWhiteSpace(type.NamedUnionDiscriminatorProperty))
            {
                continue;
            }

            var discriminatorProperty = type.NamedUnionDiscriminatorProperty;
            var cases = type.UnionCases
                .Where(unionCase =>
                    unionCase.MatchStrategy == "property-discriminator" &&
                    string.Equals(unionCase.DiscriminatorProperty, discriminatorProperty, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(unionCase.DiscriminatorValue))
                .ToArray();

            if (cases.Length == 0)
            {
                continue;
            }

            var duplicateValues = cases
                .GroupBy(static unionCase => unionCase.DiscriminatorValue!, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .ToHashSet(StringComparer.Ordinal);

            yield return new NormalizedTelegramConstantGroup(
                ToUnionConstantGroupName(type.Name, discriminatorProperty),
                $"Known Telegram Bot API {type.Name} {discriminatorProperty} values.",
                cases
                    .Select(unionCase => new NormalizedTelegramConstantSource(unionCase.RawType, discriminatorProperty))
                    .OrderBy(static source => source.TypeName, StringComparer.Ordinal)
                    .ThenBy(static source => source.TelegramName, StringComparer.Ordinal)
                    .ToArray(),
                cases
                    .Select(unionCase => ToUnionConstantValue(type.Name, unionCase, duplicateValues))
                    .OrderBy(static value => value.Name, StringComparer.Ordinal)
                    .ThenBy(static value => value.TelegramValue, StringComparer.Ordinal)
                    .ToArray());
        }
    }

    private static IReadOnlyList<NormalizedTelegramConstantGroup> MergeGroups(
        IReadOnlyList<NormalizedTelegramConstantGroup> groups)
    {
        return groups
            .GroupBy(static group => group.Name, StringComparer.Ordinal)
            .Select(static group =>
            {
                var first = group.First();

                return new NormalizedTelegramConstantGroup(
                    first.Name,
                    first.Summary,
                    group
                        .SelectMany(static item => item.Sources)
                        .GroupBy(static source => $"{source.TypeName}|{source.TelegramName}", StringComparer.Ordinal)
                        .Select(static sourceGroup => sourceGroup.First())
                        .OrderBy(static source => source.TypeName, StringComparer.Ordinal)
                        .ThenBy(static source => source.TelegramName, StringComparer.Ordinal)
                        .ToArray(),
                    group
                        .SelectMany(static item => item.Values)
                        .GroupBy(static value => $"{value.Name}|{value.TelegramValue}", StringComparer.Ordinal)
                        .Select(static valueGroup => valueGroup.First())
                        .OrderBy(static value => value.Name, StringComparer.Ordinal)
                        .ThenBy(static value => value.TelegramValue, StringComparer.Ordinal)
                        .ToArray());
            })
            .ToArray();
    }

    private static NormalizedTelegramConstantValue ToUnionConstantValue(
        string ownerTypeName,
        NormalizedTelegramUnionCase unionCase,
        IReadOnlySet<string> duplicateValues)
    {
        var telegramValue = unionCase.DiscriminatorValue
            ?? throw new InvalidOperationException($"The union case '{unionCase.RawType}' is missing discriminator value metadata.");
        var name = duplicateValues.Contains(telegramValue)
            ? ToCaseConstantName(ownerTypeName, unionCase.RawType)
            : ToPascalCase(telegramValue);

        return new NormalizedTelegramConstantValue(name, telegramValue);
    }

    private static string ToUnionConstantGroupName(string ownerTypeName, string discriminatorProperty)
    {
        var rule = DiscriminatorNameRules.FirstOrDefault(rule =>
            rule.DiscriminatorProperty.Equals(discriminatorProperty, StringComparison.Ordinal));

        if (rule is null)
        {
            return ownerTypeName + ToPascalCase(discriminatorProperty) + "Values";
        }

        return ownerTypeName.EndsWith(rule.OwnerTypeSuffix, StringComparison.Ordinal)
            ? ownerTypeName[..^rule.OwnerTypeSuffix.Length] + rule.GroupNameSuffix
            : ownerTypeName + rule.GroupNameSuffix;
    }

    private static string ToCaseConstantName(string ownerTypeName, string caseTypeName)
    {
        var name = caseTypeName.StartsWith(ownerTypeName, StringComparison.Ordinal)
            ? caseTypeName[ownerTypeName.Length..]
            : caseTypeName;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                $"The union case '{caseTypeName}' cannot be converted to a constant name for '{ownerTypeName}'.");
        }

        return name;
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

    private sealed record DiscriminatorNameRule(
        string DiscriminatorProperty,
        string OwnerTypeSuffix,
        string GroupNameSuffix);
}
