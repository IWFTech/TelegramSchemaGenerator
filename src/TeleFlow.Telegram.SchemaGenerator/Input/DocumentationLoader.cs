using System.Net.Http;

namespace TeleFlow.Telegram.SchemaGenerator.Input;

internal static class DocumentationLoader
{
    public static async Task<string> LoadHtmlAsync(GeneratorArguments arguments, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(arguments.InputHtmlPath))
        {
            return await File.ReadAllTextAsync(arguments.InputHtmlPath, cancellationToken).ConfigureAwait(false);
        }

        var url = arguments.SourceUrl ?? GeneratorArguments.DefaultSourceUrl;

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TeleFlow.SchemaGenerator/1.0");
        return await httpClient.GetStringAsync(new Uri(url, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
    }
}
