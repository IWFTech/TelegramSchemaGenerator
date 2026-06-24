using System.Text;
using System.Text.Json;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Writers;

internal static class SnapshotWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static void Write(string path, RawTelegramApiSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, SerializerOptions), new UTF8Encoding(false));
    }

    public static void Write(string path, NormalizedTelegramSchema snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, SerializerOptions), new UTF8Encoding(false));
    }
}
