namespace TeleFlow.Telegram.SchemaGenerator.Configuration;

/// <summary>
/// The compiled, immutable semantic configuration used between parsing and normalization.
/// It contains human-owned naming and schema interpretation decisions without owning parser algorithms.
/// </summary>
internal sealed record GeneratorConfiguration(
    IReadOnlyList<AnonymousUnionRule> AnonymousUnions,
    IReadOnlyList<NamedUnionRule> NamedUnions,
    IReadOnlyList<ConstantGroupRule> ConstantGroups,
    IReadOnlyList<DiscriminatorNameRule> DiscriminatorNames,
    IReadOnlyList<string> NamedUnionDiscriminatorProperties)
{
    public static GeneratorConfiguration Empty { get; } = new(
        Array.Empty<AnonymousUnionRule>(),
        Array.Empty<NamedUnionRule>(),
        Array.Empty<ConstantGroupRule>(),
        Array.Empty<DiscriminatorNameRule>(),
        Array.Empty<string>());
}

internal enum UnionEvolutionPolicy
{
    Exact,
    AdditionsOnly
}

internal enum NamedUnionStrategy
{
    MaybeInaccessibleMessage,
    PropertyDiscriminator,
    RequiredProperties
}

internal sealed record AnonymousUnionRule(
    string PublicName,
    IReadOnlySet<string> Members,
    UnionEvolutionPolicy EvolutionPolicy);

internal sealed record NamedUnionRule(
    string TypeName,
    NamedUnionStrategy Strategy,
    string? DiscriminatorProperty);

internal sealed record ConstantGroupRule(
    string Name,
    string Summary,
    string ValuePattern,
    IReadOnlyList<ConstantSourceRule> Sources,
    IReadOnlyList<string> StaticValues);

internal sealed record ConstantSourceRule(string TypeName, string PropertyName);

internal sealed record DiscriminatorNameRule(
    string PropertyName,
    string OwnerTypeSuffix,
    string GroupNameSuffix);
