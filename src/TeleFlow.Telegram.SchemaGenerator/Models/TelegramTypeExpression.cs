namespace TeleFlow.Telegram.SchemaGenerator.Models;

internal abstract record TelegramTypeExpression(string Kind, string Text);

internal sealed record PrimitiveTelegramTypeExpression(string Name)
    : TelegramTypeExpression("primitive", Name);

internal sealed record NamedTelegramTypeExpression(string Name)
    : TelegramTypeExpression("named", Name);

internal sealed record ArrayTelegramTypeExpression(TelegramTypeExpression ElementType)
    : TelegramTypeExpression("array", $"Array of {ElementType.Text}");

internal sealed record UnionTelegramTypeExpression(IReadOnlyList<TelegramTypeExpression> Members)
    : TelegramTypeExpression("union", string.Join(" or ", Members.Select(static member => member.Text)));

internal sealed record UnresolvedTelegramTypeExpression(string SourceText, string Reason)
    : TelegramTypeExpression("unresolved", SourceText);
