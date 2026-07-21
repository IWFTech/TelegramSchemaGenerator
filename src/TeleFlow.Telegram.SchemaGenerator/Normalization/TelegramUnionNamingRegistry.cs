namespace TeleFlow.Telegram.SchemaGenerator.Normalization;

/// <summary>
/// Assigns stable public names to documented anonymous Telegram union expressions
/// whose mechanical fallback names would be opaque to consumers.
/// </summary>
internal static class TelegramUnionNamingRegistry
{
    private static readonly Dictionary<string, string> SemanticAnonymousUnionNames = new(StringComparer.Ordinal)
    {
        ["InputMediaAudio or InputMediaDocument or InputMediaLivePhoto or InputMediaPhoto or InputMediaVideo"] = "InputMediaGroupItem",
        ["InputMediaAnimation or InputMediaAudio or InputMediaPhoto or InputMediaVideo or InputMediaVoiceNote"] = "InputRichMessageMediaItem"
    };

    public static bool TryGetSemanticAnonymousUnionName(string expression, out string name)
    {
        return SemanticAnonymousUnionNames.TryGetValue(expression, out name!);
    }
}
