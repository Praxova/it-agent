using System.Text.Json;
using System.Text.Json.Serialization;

namespace LucidAdmin.Core.Security;

/// <summary>
/// JSON converter for SecretString that writes "[REDACTED]" on serialization
/// and creates a SecretString from the JSON string value on deserialization.
/// </summary>
public class SecretStringJsonConverter : JsonConverter<SecretString>
{
    /// <summary>
    /// Reads a JSON string and wraps it in a SecretString.
    /// </summary>
    public override SecretString? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value != null ? new SecretString(value) : null;
    }

    /// <summary>
    /// Writes "[REDACTED]" instead of the actual secret value.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, SecretString value, JsonSerializerOptions options)
    {
        writer.WriteStringValue("[REDACTED]");
    }
}
