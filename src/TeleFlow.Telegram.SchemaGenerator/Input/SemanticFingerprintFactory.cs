using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Input;

/// <summary>
/// Produces a deterministic fingerprint for the normalized Telegram schema.
/// Volatile capture metadata is excluded so documentation timestamps do not create refresh noise.
/// </summary>
internal static class SemanticFingerprintFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public static string Create(NormalizedTelegramSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var semanticPayload = new
        {
            schema.Types,
            schema.Methods,
            schema.Abstractions,
            schema.ConstantGroups
        };
        var json = JsonSerializer.Serialize(semanticPayload, SerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
