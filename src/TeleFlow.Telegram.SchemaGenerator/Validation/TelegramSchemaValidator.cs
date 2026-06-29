using System.Text.RegularExpressions;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Validation;

internal static class TelegramSchemaValidator
{
    private static readonly Regex Sha256Regex = new("^[0-9a-f]{64}$", RegexOptions.Compiled);
    private static readonly Regex BotApiVersionRegex = new("^\\d+(?:\\.\\d+)*$", RegexOptions.Compiled);
    private static readonly Regex IsoDateRegex = new("^\\d{4}-\\d{2}-\\d{2}$", RegexOptions.Compiled);
    private static readonly Regex ChangelogAnchorRegex = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    public static void Validate(RawTelegramApiSnapshot raw)
    {
        ValidateMetadata(raw.Metadata, requireVersions: false, subject: "raw schema snapshot");

        foreach (var category in raw.Categories)
        {
            if (string.IsNullOrWhiteSpace(category.Anchor) || string.IsNullOrWhiteSpace(category.Title))
            {
                throw new InvalidOperationException("A raw Telegram category is missing a stable anchor or title.");
            }

            foreach (var section in category.Sections)
            {
                if (string.IsNullOrWhiteSpace(section.Anchor) || string.IsNullOrWhiteSpace(section.Title))
                {
                    throw new InvalidOperationException($"A section in category '{category.Anchor}' is missing a stable anchor or title.");
                }

                if (category.IsSchemaBearing && section.Classification == "ignored")
                {
                    if (string.IsNullOrWhiteSpace(section.IgnoreReason))
                    {
                        throw new InvalidOperationException(
                            $"The schema-bearing section '{section.Title}' ({section.Anchor}) in category '{category.Anchor}' was ignored without an explicit reason.");
                    }
                }

                if (!category.IsSchemaBearing && section.Classification != "ignored")
                {
                    throw new InvalidOperationException(
                        $"The non-schema section '{section.Title}' ({section.Anchor}) in category '{category.Anchor}' leaked into schema extraction as '{section.Classification}'.");
                }
            }
        }
    }

    public static void Validate(NormalizedTelegramSchema schema)
    {
        ValidateMetadata(schema.Metadata, requireVersions: true, subject: "normalized schema snapshot");

        foreach (var method in schema.Methods)
        {
            if (method.ResultExpression.Kind == "unresolved")
            {
                throw new InvalidOperationException(
                    $"The method '{method.Name}' ({method.Anchor}) has an unresolved result type expression '{method.RawResultType}'.");
            }

            foreach (var parameter in method.Parameters)
            {
                if (parameter.TypeExpression.Kind == "unresolved")
                {
                    throw new InvalidOperationException(
                        $"The method '{method.Name}' ({method.Anchor}) has an unresolved parameter type expression '{parameter.RawType}' for '{parameter.TelegramName}'.");
                }
            }
        }

        foreach (var type in schema.Types)
        {
            if (type.UnionMembers.Count > 0)
            {
                if (type.Kind != "named-union")
                {
                    throw new InvalidOperationException(
                        $"The type '{type.Name}' has union members but is not classified as a named union.");
                }

                if (string.IsNullOrWhiteSpace(type.NamedUnionStrategy))
                {
                    throw new InvalidOperationException(
                        $"The named union type '{type.Name}' is missing named union strategy metadata.");
                }

                if (type.NamedUnionStrategy == "property-discriminator" &&
                    string.IsNullOrWhiteSpace(type.NamedUnionDiscriminatorProperty))
                {
                    throw new InvalidOperationException(
                        $"The named union type '{type.Name}' is missing discriminator property metadata.");
                }

                ValidateUnionCases(type.Name, type.UnionCases);
            }

            if (type.Kind == "named-union" && type.UnionCases.Count == 0)
            {
                throw new InvalidOperationException($"The named union type '{type.Name}' does not declare any union cases.");
            }

            if (type.Kind == "pseudo-upload" && type.Name != "InputFile")
            {
                throw new InvalidOperationException($"Unexpected pseudo upload type '{type.Name}'.");
            }

            foreach (var property in type.Properties)
            {
                if (property.TypeExpression.Kind == "unresolved")
                {
                    throw new InvalidOperationException(
                        $"The type '{type.Name}' ({type.Anchor}) has an unresolved property type expression '{property.RawType}' for '{property.TelegramName}'.");
                }
            }
        }

        var invalidOpaqueAbstractions = schema.Abstractions
            .Where(static abstraction => abstraction.Kind == "union")
            .Select(static abstraction => abstraction.Name)
            .Where(static name => Regex.IsMatch(name, @"Union[0-9A-F]{6}$", RegexOptions.CultureInvariant))
            .ToArray();

        if (invalidOpaqueAbstractions.Length > 0)
        {
            throw new InvalidOperationException(
                "The normalized schema contains prohibited opaque public union names: " + string.Join(", ", invalidOpaqueAbstractions));
        }

        foreach (var abstraction in schema.Abstractions.Where(static abstraction => abstraction.Kind == "union"))
        {
            ValidateUnionCases(abstraction.Name, abstraction.UnionCases);
        }

        ValidateConstantGroups(schema.ConstantGroups);
    }

    private static void ValidateConstantGroups(IReadOnlyList<NormalizedTelegramConstantGroup>? constantGroups)
    {
        if (constantGroups is null)
        {
            throw new InvalidOperationException("The normalized schema snapshot is missing ConstantGroups.");
        }

        var duplicateGroups = constantGroups
            .GroupBy(static group => group.Name, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicateGroups.Length > 0)
        {
            throw new InvalidOperationException("The normalized schema contains duplicate constant groups: " + string.Join(", ", duplicateGroups));
        }

        foreach (var group in constantGroups)
        {
            if (string.IsNullOrWhiteSpace(group.Name) ||
                string.IsNullOrWhiteSpace(group.Summary) ||
                group.Values.Count == 0)
            {
                throw new InvalidOperationException("The normalized schema contains an incomplete constant group.");
            }

            foreach (var source in group.Sources)
            {
                if (string.IsNullOrWhiteSpace(source.TypeName) ||
                    string.IsNullOrWhiteSpace(source.TelegramName))
                {
                    throw new InvalidOperationException($"The constant group '{group.Name}' contains an incomplete source definition.");
                }
            }

            var duplicateValueNames = group.Values
                .GroupBy(static value => value.Name, StringComparer.Ordinal)
                .Where(static valueGroup => valueGroup.Count() > 1)
                .Select(static valueGroup => valueGroup.Key)
                .ToArray();

            if (duplicateValueNames.Length > 0)
            {
                throw new InvalidOperationException(
                    $"The constant group '{group.Name}' contains duplicate value names: " + string.Join(", ", duplicateValueNames));
            }

            foreach (var value in group.Values)
            {
                if (string.IsNullOrWhiteSpace(value.Name) ||
                    string.IsNullOrWhiteSpace(value.TelegramValue))
                {
                    throw new InvalidOperationException($"The constant group '{group.Name}' contains an incomplete value definition.");
                }
            }
        }
    }

    private static void ValidateUnionCases(string ownerName, IReadOnlyList<NormalizedTelegramUnionCase> unionCases)
    {
        if (unionCases.Count == 0)
        {
            throw new InvalidOperationException($"The union '{ownerName}' does not declare any union cases.");
        }

        foreach (var unionCase in unionCases)
        {
            if (string.IsNullOrWhiteSpace(unionCase.Name) ||
                string.IsNullOrWhiteSpace(unionCase.RawType) ||
                string.IsNullOrWhiteSpace(unionCase.CSharpType) ||
                string.IsNullOrWhiteSpace(unionCase.MatchStrategy))
            {
                throw new InvalidOperationException($"The union '{ownerName}' contains an incomplete case definition.");
            }

            if (unionCase.MatchStrategy == "property-discriminator" &&
                (string.IsNullOrWhiteSpace(unionCase.DiscriminatorProperty) ||
                 string.IsNullOrWhiteSpace(unionCase.DiscriminatorValue)))
            {
                throw new InvalidOperationException(
                    $"The union '{ownerName}' case '{unionCase.RawType}' is missing discriminator property or literal value metadata.");
            }

            if (unionCase.MatchStrategy == "required-properties" &&
                unionCase.RequiredProperties.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The union '{ownerName}' case '{unionCase.RawType}' is missing required-property match metadata.");
            }
        }

        var ambiguousDiscriminatorCases = unionCases
            .Where(static unionCase => unionCase.MatchStrategy == "property-discriminator")
            .GroupBy(static unionCase => $"{unionCase.DiscriminatorProperty}|{unionCase.DiscriminatorValue}", StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Where(static group =>
            {
                var signatures = group
                    .Select(static unionCase => string.Join("|", unionCase.RequiredProperties))
                    .ToArray();

                return signatures.Any(static signature => string.IsNullOrWhiteSpace(signature)) ||
                       signatures.Distinct(StringComparer.Ordinal).Count() != signatures.Length;
            })
            .Select(static group => group.Key)
            .ToArray();

        if (ambiguousDiscriminatorCases.Length > 0)
        {
            throw new InvalidOperationException(
                $"The union '{ownerName}' has ambiguous discriminator cases that require unique required-property refinement: {string.Join(", ", ambiguousDiscriminatorCases)}.");
        }

        var duplicateRequiredPropertyCases = unionCases
            .Where(static unionCase => unionCase.MatchStrategy == "required-properties")
            .GroupBy(static unionCase => string.Join("|", unionCase.RequiredProperties), StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicateRequiredPropertyCases.Length > 0)
        {
            throw new InvalidOperationException(
                $"The union '{ownerName}' has ambiguous required-property cases: {string.Join(", ", duplicateRequiredPropertyCases)}.");
        }
    }

    private static void ValidateMetadata(TelegramSchemaMetadata? metadata, bool requireVersions, string subject)
    {
        if (metadata is null)
        {
            throw new InvalidOperationException($"The {subject} is missing Metadata.");
        }

        if (string.IsNullOrWhiteSpace(metadata.SourceUrl))
        {
            throw new InvalidOperationException($"The {subject} is missing SourceUrl metadata.");
        }

        if (metadata.SourceCapturedAtUtc == default)
        {
            throw new InvalidOperationException($"The {subject} is missing SourceCapturedAtUtc metadata.");
        }

        if (string.IsNullOrWhiteSpace(metadata.SourceSha256) || !Sha256Regex.IsMatch(metadata.SourceSha256))
        {
            throw new InvalidOperationException($"The {subject} is missing a valid SourceSha256 metadata value.");
        }

        if (string.IsNullOrWhiteSpace(metadata.TelegramBotApiVersion) || !BotApiVersionRegex.IsMatch(metadata.TelegramBotApiVersion))
        {
            throw new InvalidOperationException($"The {subject} is missing a valid TelegramBotApiVersion metadata value.");
        }

        if (string.IsNullOrWhiteSpace(metadata.TelegramBotApiReleasedAt) || !IsoDateRegex.IsMatch(metadata.TelegramBotApiReleasedAt))
        {
            throw new InvalidOperationException($"The {subject} is missing a valid TelegramBotApiReleasedAt metadata value.");
        }

        if (string.IsNullOrWhiteSpace(metadata.TelegramBotApiChangelogAnchor) || !ChangelogAnchorRegex.IsMatch(metadata.TelegramBotApiChangelogAnchor))
        {
            throw new InvalidOperationException($"The {subject} is missing a valid TelegramBotApiChangelogAnchor metadata value.");
        }

        if (!requireVersions)
        {
            return;
        }

        if (metadata.SchemaVersion is null or <= 0)
        {
            throw new InvalidOperationException($"The {subject} is missing a valid SchemaVersion metadata value.");
        }

        if (metadata.GeneratorVersion is null or <= 0)
        {
            throw new InvalidOperationException($"The {subject} is missing a valid GeneratorVersion metadata value.");
        }
    }
}
