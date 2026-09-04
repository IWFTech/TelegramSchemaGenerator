using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TeleFlow.Telegram.SchemaGenerator.Configuration;

/// <summary>
/// Loads and validates the repository-owned generator configuration at the normalization boundary.
/// Invalid semantic configuration is rejected before it can influence generated public APIs.
/// </summary>
internal static class GeneratorConfigurationLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static GeneratorConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new GeneratorConfigurationException($"Generator configuration was not found at '{path}'.");
        }

        var source = File.ReadAllText(path);
        GeneratorConfigurationDocument? document;
        try
        {
            document = Deserializer.Deserialize<GeneratorConfigurationDocument>(source);
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            throw new GeneratorConfigurationException(
                $"Generator configuration could not be parsed: {exception.Message}",
                exception);
        }

        if (document is null)
        {
            throw new GeneratorConfigurationException("Generator configuration is empty.");
        }

        return Compile(document, path);
    }

    private static GeneratorConfiguration Compile(GeneratorConfigurationDocument document, string path)
    {
        var anonymousUnions = document.AnonymousUnions
            .Select((rule, index) => CompileAnonymousUnion(rule, index, path))
            .ToArray();
        var duplicateUnionNames = anonymousUnions
            .GroupBy(static rule => rule.PublicName, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicateUnionNames.Length > 0)
        {
            throw new GeneratorConfigurationException(
                $"Generator configuration contains duplicate anonymous union names: {string.Join(", ", duplicateUnionNames)}.");
        }

        var namedUnions = document.NamedUnions
            .Select((rule, index) => CompileNamedUnion(rule, index, path))
            .ToArray();
        var duplicateNamedUnionTypes = namedUnions
            .GroupBy(static rule => rule.TypeName, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicateNamedUnionTypes.Length > 0)
        {
            throw new GeneratorConfigurationException(
                $"Generator configuration contains duplicate named union types: {string.Join(", ", duplicateNamedUnionTypes)}.");
        }
        var constantGroups = document.ConstantGroups
            .Select((rule, index) => CompileConstantGroup(rule, index, path))
            .ToArray();
        var duplicateConstantNames = constantGroups
            .GroupBy(static rule => rule.Name, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicateConstantNames.Length > 0)
        {
            throw new GeneratorConfigurationException(
                $"Generator configuration contains duplicate constant group names: {string.Join(", ", duplicateConstantNames)}.");
        }

        var discriminatorNames = document.DiscriminatorNames
            .Select((rule, index) => CompileDiscriminatorName(rule, index, path))
            .ToArray();
        var discriminatorProperties = document.NamedUnionDiscriminatorProperties
            .Select((propertyName, index) => Require(
                propertyName,
                $"namedUnionDiscriminatorProperties[{index}]",
                path))
            .ToArray();

        if (discriminatorProperties.Length == 0)
        {
            throw Invalid(path, "namedUnionDiscriminatorProperties must contain at least one property.");
        }

        return new GeneratorConfiguration(
            anonymousUnions,
            namedUnions,
            constantGroups,
            discriminatorNames,
            discriminatorProperties);
    }

    private static AnonymousUnionRule CompileAnonymousUnion(
        AnonymousUnionDocument rule,
        int index,
        string path)
    {
        var publicName = Require(rule.PublicName, $"anonymousUnions[{index}].publicName", path);
        if (rule.Members.Count == 0)
        {
            throw Invalid(path, $"anonymousUnions[{index}].members must contain at least one member.");
        }

        var members = rule.Members
            .Select((member, memberIndex) => Require(member, $"anonymousUnions[{index}].members[{memberIndex}]", path))
            .ToHashSet(StringComparer.Ordinal);
        if (members.Count != rule.Members.Count)
        {
            throw Invalid(path, $"anonymousUnions[{index}].members must not contain duplicates.");
        }

        var evolutionPolicy = rule.Evolution switch
        {
            "exact" => UnionEvolutionPolicy.Exact,
            "additions-only" => UnionEvolutionPolicy.AdditionsOnly,
            _ => throw Invalid(path, $"anonymousUnions[{index}].evolution must be 'exact' or 'additions-only'.")
        };

        return new AnonymousUnionRule(publicName, members, evolutionPolicy);
    }

    private static NamedUnionRule CompileNamedUnion(NamedUnionDocument rule, int index, string path)
    {
        var typeName = Require(rule.TypeName, $"namedUnions[{index}].typeName", path);
        var strategy = Require(rule.Strategy, $"namedUnions[{index}].strategy", path);
        var parsedStrategy = strategy switch
        {
            "maybe-inaccessible-message" => NamedUnionStrategy.MaybeInaccessibleMessage,
            "property-discriminator" => NamedUnionStrategy.PropertyDiscriminator,
            "required-properties" => NamedUnionStrategy.RequiredProperties,
            _ => throw Invalid(path, $"namedUnions[{index}].strategy is unknown: '{strategy}'.")
        };

        if (parsedStrategy == NamedUnionStrategy.PropertyDiscriminator &&
            string.IsNullOrWhiteSpace(rule.DiscriminatorProperty))
        {
            throw Invalid(path, $"namedUnions[{index}].discriminatorProperty is required for property-discriminator.");
        }

        return new NamedUnionRule(typeName, parsedStrategy, rule.DiscriminatorProperty);
    }

    private static ConstantGroupRule CompileConstantGroup(ConstantGroupDocument rule, int index, string path)
    {
        var name = Require(rule.Name, $"constantGroups[{index}].name", path);
        var summary = Require(rule.Summary, $"constantGroups[{index}].summary", path);
        var valuePattern = Require(rule.ValuePattern, $"constantGroups[{index}].valuePattern", path);
        Regex valueRegex;
        try
        {
            valueRegex = new Regex(valuePattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException exception)
        {
            throw Invalid(path, $"constantGroups[{index}].valuePattern is not a valid regular expression: {exception.Message}");
        }

        if (!valueRegex.GetGroupNames().Contains("value", StringComparer.Ordinal))
        {
            throw Invalid(path, $"constantGroups[{index}].valuePattern must define a named 'value' capture group.");
        }
        if (rule.Sources.Count == 0)
        {
            throw Invalid(path, $"constantGroups[{index}].sources must contain at least one source.");
        }

        var sources = rule.Sources
            .Select((source, sourceIndex) =>
            {
                var typeName = Require(source.TypeName, $"constantGroups[{index}].sources[{sourceIndex}].typeName", path);
                var propertyName = Require(source.PropertyName, $"constantGroups[{index}].sources[{sourceIndex}].propertyName", path);
                return new ConstantSourceRule(typeName, propertyName);
            })
            .ToArray();

        return new ConstantGroupRule(name, summary, valuePattern, sources, rule.StaticValues.ToArray());
    }

    private static DiscriminatorNameRule CompileDiscriminatorName(
        DiscriminatorNameDocument rule,
        int index,
        string path)
    {
        var propertyName = Require(rule.PropertyName, $"discriminatorNames[{index}].propertyName", path);
        var ownerTypeSuffix = Require(rule.OwnerTypeSuffix, $"discriminatorNames[{index}].ownerTypeSuffix", path);
        var groupNameSuffix = Require(rule.GroupNameSuffix, $"discriminatorNames[{index}].groupNameSuffix", path);
        return new DiscriminatorNameRule(propertyName, ownerTypeSuffix, groupNameSuffix);
    }

    private static string Require(string? value, string name, string path)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw Invalid(path, $"{name} must not be empty.")
            : value;
    }

    private static GeneratorConfigurationException Invalid(string path, string message)
    {
        return new GeneratorConfigurationException($"Invalid generator configuration '{path}': {message}");
    }

    private sealed class GeneratorConfigurationDocument
    {
        public List<AnonymousUnionDocument> AnonymousUnions { get; init; } = [];
        public List<NamedUnionDocument> NamedUnions { get; init; } = [];
        public List<ConstantGroupDocument> ConstantGroups { get; init; } = [];
        public List<DiscriminatorNameDocument> DiscriminatorNames { get; init; } = [];
        public List<string> NamedUnionDiscriminatorProperties { get; init; } = [];
    }

    private sealed class AnonymousUnionDocument
    {
        public string? PublicName { get; init; }
        public List<string> Members { get; init; } = [];
        public string Evolution { get; init; } = "exact";
    }

    private sealed class NamedUnionDocument
    {
        public string? TypeName { get; init; }
        public string? Strategy { get; init; }
        public string? DiscriminatorProperty { get; init; }
    }

    private sealed class ConstantGroupDocument
    {
        public string? Name { get; init; }
        public string? Summary { get; init; }
        public string? ValuePattern { get; init; }
        public List<ConstantSourceDocument> Sources { get; init; } = [];
        public List<string> StaticValues { get; init; } = [];
    }

    private sealed class ConstantSourceDocument
    {
        public string? TypeName { get; init; }
        public string? PropertyName { get; init; }
    }

    private sealed class DiscriminatorNameDocument
    {
        public string? PropertyName { get; init; }
        public string? OwnerTypeSuffix { get; init; }
        public string? GroupNameSuffix { get; init; }
    }
}
