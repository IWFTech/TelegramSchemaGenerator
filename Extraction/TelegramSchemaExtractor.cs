using System.Text.RegularExpressions;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Extraction;

internal static class TelegramSchemaExtractor
{
    private static readonly HashSet<string> SchemaBearingCategories =
    [
        "getting-updates",
        "available-types",
        "available-methods",
        "updating-messages",
        "stickers",
        "rich-messages",
        "inline-mode",
        "payments",
        "telegram-passport",
        "games"
    ];

    private static readonly HashSet<string> NonSchemaCategories =
    [
        "recent-changes",
        "authorizing-your-bot",
        "making-requests",
        "using-a-local-bot-api-server"
    ];

    private static readonly Regex LowerCamelCaseRegex = new(@"^[a-z][A-Za-z0-9]*$", RegexOptions.Compiled);
    private static readonly Regex PascalCaseRegex = new(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.Compiled);

    public static RawTelegramApiSnapshot Extract(TelegramDocument document, TelegramSchemaMetadata metadata)
    {
        var categories = document.Categories
            .Select(ExtractCategory)
            .ToArray();

        return new RawTelegramApiSnapshot(metadata, categories);
    }

    private static RawTelegramCategory ExtractCategory(TelegramCategoryNode category)
    {
        var isSchemaBearing = SchemaBearingCategories.Contains(category.Anchor);
        var isNonSchema = NonSchemaCategories.Contains(category.Anchor);

        if (!isSchemaBearing && !isNonSchema)
        {
            throw new InvalidOperationException($"The category '{category.Title}' ({category.Anchor}) is not declared as schema-bearing or non-schema.");
        }

        var sections = category.Sections
            .Select(section => ExtractSection(category, section, isSchemaBearing, isNonSchema))
            .ToArray();

        return new RawTelegramCategory(
            category.Anchor,
            category.Title,
            isSchemaBearing,
            IsMixedSchemaCategory(category.Anchor),
            sections);
    }

    private static RawTelegramSection ExtractSection(
        TelegramCategoryNode category,
        TelegramSectionNode section,
        bool isSchemaBearing,
        bool isNonSchema)
    {
        var classification = ClassifySection(category, section, isSchemaBearing, isNonSchema, out var ignoreReason);

        return new RawTelegramSection(
            section.Anchor,
            section.Title,
            classification,
            ignoreReason,
            section.Blocks.Select(MapBlock).ToArray());
    }

    private static string ClassifySection(
        TelegramCategoryNode category,
        TelegramSectionNode section,
        bool isSchemaBearing,
        bool isNonSchema,
        out string? ignoreReason)
    {
        ignoreReason = null;

        if (isNonSchema)
        {
            ignoreReason = $"Section belongs to non-schema category '{category.Anchor}'.";
            return "ignored";
        }

        var titleSignal = ClassifyHeadingShape(section.Title);
        var tableSignal = ClassifyTables(section.Blocks);
        var wordingSignal = ClassifyWording(section.Blocks);

        string classification;
        if (category.Anchor == "available-methods")
        {
            if (titleSignal == "method")
            {
                classification = "method";
            }
            else if (tableSignal == "method")
            {
                classification = "method";
            }
            else if (wordingSignal == "method")
            {
                classification = "method";
            }
            else
            {
                ignoreReason = "Section belongs to 'available-methods' but does not have a method heading shape or method schema table.";
                return "ignored";
            }
        }
        else if (category.Anchor == "available-types")
        {
            if (titleSignal == "type")
            {
                classification = "type";
            }
            else if (tableSignal == "type")
            {
                classification = "type";
            }
            else if (wordingSignal == "type")
            {
                classification = "type";
            }
            else
            {
                ignoreReason = "Section belongs to 'available-types' but does not have a type heading shape or type schema table.";
                return "ignored";
            }
        }
        else if (titleSignal is not null)
        {
            classification = titleSignal;
        }
        else if (tableSignal is not null)
        {
            classification = tableSignal;
        }
        else if (wordingSignal is not null)
        {
            classification = wordingSignal;
        }
        else
        {
            ignoreReason = $"Section in schema-bearing category '{category.Anchor}' does not match method/type heading shape or schema table layout.";
            return "ignored";
        }

        ValidateSignals(section, classification, tableSignal, wordingSignal);
        return classification;
    }

    private static void ValidateSignals(
        TelegramSectionNode section,
        string classification,
        string? tableSignal,
        string? wordingSignal)
    {
        if (tableSignal is not null && tableSignal != classification)
        {
            throw new InvalidOperationException(
                $"The section '{section.Title}' ({section.Anchor}) has conflicting table/classification signals: expected '{classification}', table suggests '{tableSignal}'.");
        }

        if (wordingSignal is not null && wordingSignal != classification)
        {
            throw new InvalidOperationException(
                $"The section '{section.Title}' ({section.Anchor}) has conflicting wording/classification signals: expected '{classification}', wording suggests '{wordingSignal}'.");
        }
    }

    private static string? ClassifyHeadingShape(string title)
    {
        if (LowerCamelCaseRegex.IsMatch(title))
        {
            return "method";
        }

        if (PascalCaseRegex.IsMatch(title))
        {
            return "type";
        }

        return null;
    }

    private static string? ClassifyTables(IReadOnlyList<TelegramBlockNode> blocks)
    {
        foreach (var table in blocks.Where(static block => block.Kind == "table").Select(static block => block.Table).Where(static table => table is not null))
        {
            var headers = table!.Headers.Select(static header => header.Text).ToArray();
            if (MatchesHeaders(headers, "Parameter", "Type", "Required", "Description"))
            {
                return "method";
            }

            if (MatchesHeaders(headers, "Field", "Type", "Description"))
            {
                return "type";
            }
        }

        return null;
    }

    private static string? ClassifyWording(IReadOnlyList<TelegramBlockNode> blocks)
    {
        var firstParagraph = blocks.FirstOrDefault(static block => block.Kind == "paragraph")?.Text;
        if (string.IsNullOrWhiteSpace(firstParagraph))
        {
            return null;
        }

        if (firstParagraph.StartsWith("Use this method", StringComparison.OrdinalIgnoreCase) ||
            firstParagraph.StartsWith("A simple method", StringComparison.OrdinalIgnoreCase))
        {
            return "method";
        }

        if (firstParagraph.StartsWith("This object", StringComparison.OrdinalIgnoreCase) ||
            firstParagraph.StartsWith("Describes", StringComparison.OrdinalIgnoreCase) ||
            firstParagraph.StartsWith("Represents", StringComparison.OrdinalIgnoreCase) ||
            firstParagraph.StartsWith("Upon receiving", StringComparison.OrdinalIgnoreCase) ||
            firstParagraph.StartsWith("Contains information", StringComparison.OrdinalIgnoreCase) ||
            firstParagraph.StartsWith("This object defines", StringComparison.OrdinalIgnoreCase) ||
            firstParagraph.StartsWith("A placeholder", StringComparison.OrdinalIgnoreCase))
        {
            return "type";
        }

        return null;
    }

    private static bool MatchesHeaders(string[] headers, params string[] expected)
    {
        if (headers.Length < expected.Length)
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (!headers[index].Equals(expected[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMixedSchemaCategory(string anchor)
    {
        return anchor is not "available-methods" and not "available-types";
    }

    private static RawTelegramBlock MapBlock(TelegramBlockNode block)
    {
        return new RawTelegramBlock(
            block.Kind,
            block.Text,
            block.Html,
            block.Inlines.Select(MapInline).ToArray(),
            block.Items.Select(MapListItem).ToArray(),
            block.Table is null ? null : MapTable(block.Table));
    }

    private static RawTelegramListItem MapListItem(TelegramListItemNode item)
    {
        return new RawTelegramListItem(
            item.Text,
            item.Html,
            item.Inlines.Select(MapInline).ToArray());
    }

    private static RawTelegramInline MapInline(TelegramInlineNode inline)
    {
        return new RawTelegramInline(inline.Kind, inline.Text, inline.Href, inline.Anchor);
    }

    private static RawTelegramTable MapTable(TelegramTableNode table)
    {
        return new RawTelegramTable(
            table.Headers.Select(MapCell).ToArray(),
            table.Rows.Select(static row => new RawTelegramRow(row.Cells.Select(MapCell).ToArray())).ToArray());
    }

    private static RawTelegramCell MapCell(TelegramCellNode cell)
    {
        return new RawTelegramCell(
            cell.Text,
            cell.Html,
            cell.Inlines.Select(MapInline).ToArray());
    }
}
