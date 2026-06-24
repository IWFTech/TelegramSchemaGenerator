using TeleFlow.Telegram.SchemaGenerator.Extraction;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Parsing;

internal static class TelegramDocumentationParser
{
    public static RawTelegramApiSnapshot Parse(string html, TelegramSchemaMetadata metadata)
    {
        var document = TelegramDocumentParser.Parse(html, metadata.SourceUrl);
        return TelegramSchemaExtractor.Extract(document, metadata);
    }
}
