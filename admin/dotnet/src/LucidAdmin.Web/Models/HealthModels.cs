using System.Text.Json.Serialization;

namespace LucidAdmin.Web.Models;

public record HealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("database")] string Database
);
