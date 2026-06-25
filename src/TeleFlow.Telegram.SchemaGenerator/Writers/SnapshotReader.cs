using System.Text.Json;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Writers;

internal static class SnapshotReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static RawTelegramApiSnapshot ReadRaw(string path)
    {
        return JsonSerializer.Deserialize<RawTelegramApiSnapshot>(File.ReadAllText(path), SerializerOptions)
               ?? throw new InvalidOperationException("The raw schema snapshot could not be deserialized.");
    }

    public static NormalizedTelegramSchema ReadNormalized(string path)
    {
        return JsonSerializer.Deserialize<NormalizedTelegramSchema>(File.ReadAllText(path), SerializerOptions)
               ?? throw new InvalidOperationException("The normalized schema snapshot could not be deserialized.");
    }
}
