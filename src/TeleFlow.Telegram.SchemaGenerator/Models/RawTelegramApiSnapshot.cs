namespace TeleFlow.Telegram.SchemaGenerator.Models;

internal sealed record RawTelegramApiSnapshot(
    TelegramSchemaMetadata Metadata,
    IReadOnlyList<RawTelegramCategory> Categories);

internal sealed record RawTelegramCategory(
    string Anchor,
    string Title,
    bool IsSchemaBearing,
    bool IsMixedSchemaCategory,
    IReadOnlyList<RawTelegramSection> Sections);

internal sealed record RawTelegramSection(
    string Anchor,
    string Title,
    string Classification,
    string? IgnoreReason,
    IReadOnlyList<RawTelegramBlock> Blocks);

internal sealed record RawTelegramBlock(
    string Kind,
    string Text,
    string Html,
    IReadOnlyList<RawTelegramInline> Inlines,
    IReadOnlyList<RawTelegramListItem> Items,
    RawTelegramTable? Table);

internal sealed record RawTelegramListItem(
    string Text,
    string Html,
    IReadOnlyList<RawTelegramInline> Inlines);

internal sealed record RawTelegramInline(
    string Kind,
    string Text,
    string? Href,
    string? Anchor);

internal sealed record RawTelegramTable(
    IReadOnlyList<RawTelegramCell> Headers,
    IReadOnlyList<RawTelegramRow> Rows);

internal sealed record RawTelegramRow(
    IReadOnlyList<RawTelegramCell> Cells);

internal sealed record RawTelegramCell(
    string Text,
    string Html,
    IReadOnlyList<RawTelegramInline> Inlines);
