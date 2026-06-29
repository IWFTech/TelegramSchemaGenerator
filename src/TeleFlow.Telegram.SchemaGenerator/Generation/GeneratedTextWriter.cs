namespace TeleFlow.Telegram.SchemaGenerator.Generation;

internal static class GeneratedTextWriter
{
    public static void WriteAllText(string path, string contents)
    {
        File.WriteAllText(path, NormalizeLineEndings(contents), Utf8WithoutBom.Instance);
    }

    private static string NormalizeLineEndings(string contents)
    {
        return contents
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }
}
