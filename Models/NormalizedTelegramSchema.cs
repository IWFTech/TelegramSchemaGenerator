namespace TeleFlow.Telegram.SchemaGenerator.Models;

internal sealed record NormalizedTelegramSchema(
    TelegramSchemaMetadata Metadata,
    IReadOnlyList<NormalizedTelegramType> Types,
    IReadOnlyList<NormalizedTelegramMethod> Methods,
    IReadOnlyList<NormalizedTelegramAbstraction> Abstractions);

internal sealed record NormalizedTelegramType(
    string Name,
    string Anchor,
    string Summary,
    IReadOnlyList<string> Remarks,
    string Kind,
    bool IsAliasLike,
    IReadOnlyList<string> UnionMembers,
    IReadOnlyList<NormalizedTelegramUnionCase> UnionCases,
    string? NamedUnionStrategy,
    string? NamedUnionDiscriminatorProperty,
    IReadOnlyList<NormalizedTelegramProperty> Properties);

internal sealed record NormalizedTelegramUnionCase(
    string Name,
    string RawType,
    NormalizedTelegramExpression TypeExpression,
    string CSharpType,
    string MatchStrategy,
    string? DiscriminatorProperty,
    string? DiscriminatorValue,
    IReadOnlyList<string> RequiredProperties);

internal sealed record NormalizedTelegramMethod(
    string Name,
    string Anchor,
    string TelegramMethodName,
    string Summary,
    IReadOnlyList<string> Remarks,
    string RawResultType,
    NormalizedTelegramExpression ResultExpression,
    string ResultType,
    IReadOnlyList<NormalizedTelegramProperty> Parameters);

internal sealed record NormalizedTelegramProperty(
    string Name,
    string TelegramName,
    string RawType,
    NormalizedTelegramExpression TypeExpression,
    string CSharpType,
    bool Required,
    string? LiteralValue,
    string Summary);

internal sealed record NormalizedTelegramExpression(
    string Kind,
    string Text,
    IReadOnlyList<NormalizedTelegramExpression> Members);

internal sealed record NormalizedTelegramAbstraction(
    string Name,
    string Summary,
    string Kind,
    string? RawExpression,
    string ValueShape,
    IReadOnlyList<string> Members,
    IReadOnlyList<NormalizedTelegramUnionCase> UnionCases);
