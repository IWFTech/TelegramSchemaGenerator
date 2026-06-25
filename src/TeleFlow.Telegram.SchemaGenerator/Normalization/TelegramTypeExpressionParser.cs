using System.Text.RegularExpressions;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Normalization;

internal static class TelegramTypeExpressionParser
{
    private static readonly Regex ArrayRegex = new(@"^Array of (.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UnionSeparatorRegex = new(@"\s*(?:,\s*|\s+or\s+|\s+and\s+)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> PrimitiveTokens =
    [
        "Integer",
        "Int",
        "String",
        "Boolean",
        "Float",
        "True"
    ];

    public static TelegramTypeExpression Parse(string text, IReadOnlySet<string> knownTypeNames)
    {
        return ParseCore(text.Trim(), knownTypeNames);
    }

    private static TelegramTypeExpression ParseCore(string text, IReadOnlySet<string> knownTypeNames)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new UnresolvedTelegramTypeExpression(text, "The expression is empty.");
        }

        var arrayMatch = ArrayRegex.Match(text);
        if (arrayMatch.Success)
        {
            var element = ParseCore(arrayMatch.Groups[1].Value.Trim(), knownTypeNames);
            return element is UnresolvedTelegramTypeExpression unresolved
                ? unresolved with { SourceText = text }
                : new ArrayTelegramTypeExpression(element);
        }

        var unionMembers = TrySplitUnionMembers(text);
        if (unionMembers is not null)
        {
            var members = unionMembers.Select(part => ParseCore(part, knownTypeNames)).ToArray();
            var unresolved = members.OfType<UnresolvedTelegramTypeExpression>().FirstOrDefault();
            return unresolved is not null
                ? unresolved with { SourceText = text }
                : new UnionTelegramTypeExpression(members);
        }

        if (PrimitiveTokens.Contains(text))
        {
            return new PrimitiveTelegramTypeExpression(text);
        }

        if (knownTypeNames.Contains(text))
        {
            return new NamedTelegramTypeExpression(text);
        }

        if (text.All(static character => char.IsLetterOrDigit(character) || character == '_'))
        {
            return new NamedTelegramTypeExpression(text);
        }

        return new UnresolvedTelegramTypeExpression(text, "The expression does not match the supported Telegram type grammar.");
    }

    private static string[]? TrySplitUnionMembers(string text)
    {
        if (!text.Contains(',', StringComparison.Ordinal) &&
            !text.Contains(" or ", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains(" and ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = UnionSeparatorRegex
            .Split(text)
            .Select(static part => part.Trim())
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length > 1 ? parts : null;
    }
}
