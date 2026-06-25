namespace TeleFlow.Telegram.SchemaGenerator.Normalization;

internal static class TelegramUnionNamingRegistry
{
    private static readonly Dictionary<string, string> SemanticAnonymousUnionNames = new(StringComparer.Ordinal)
    {
        ["InputMediaAudio or InputMediaDocument or InputMediaLivePhoto or InputMediaPhoto or InputMediaVideo"] = "InputMediaGroupItem"
    };

    public static bool TryGetSemanticAnonymousUnionName(string expression, out string name)
    {
        return SemanticAnonymousUnionNames.TryGetValue(expression, out name!);
    }
}
