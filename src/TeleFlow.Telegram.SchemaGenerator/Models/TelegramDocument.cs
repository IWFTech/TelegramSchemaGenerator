namespace TeleFlow.Telegram.SchemaGenerator.Models;

internal sealed record TelegramDocument(
    string SourceUrl,
    IReadOnlyList<TelegramCategoryNode> Categories);

internal sealed record TelegramCategoryNode(
    string Anchor,
    string Title,
    IReadOnlyList<TelegramSectionNode> Sections);

internal sealed record TelegramSectionNode(
    string Anchor,
    string Title,
    IReadOnlyList<TelegramBlockNode> Blocks);

internal sealed record TelegramBlockNode(
    string Kind,
    string Text,
    string Html,
    IReadOnlyList<TelegramInlineNode> Inlines,
    IReadOnlyList<TelegramListItemNode> Items,
    TelegramTableNode? Table);

internal sealed record TelegramListItemNode(
    string Text,
    string Html,
    IReadOnlyList<TelegramInlineNode> Inlines);

internal sealed record TelegramInlineNode(
    string Kind,
    string Text,
    string? Href,
    string? Anchor);

internal sealed record TelegramTableNode(
    IReadOnlyList<TelegramCellNode> Headers,
    IReadOnlyList<TelegramRowNode> Rows);

internal sealed record TelegramRowNode(
    IReadOnlyList<TelegramCellNode> Cells);

internal sealed record TelegramCellNode(
    string Text,
    string Html,
    IReadOnlyList<TelegramInlineNode> Inlines);
