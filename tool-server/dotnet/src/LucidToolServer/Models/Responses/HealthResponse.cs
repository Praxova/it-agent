using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Responses;

/// <summary>
/// Response from the health check endpoint.
/// </summary>
public record HealthResponse(
    [property: JsonPropertyName("status")] string Status,  // "healthy", "degraded", "unhealthy"
    [property: JsonPropertyName("ad_connected")] bool AdConnected,
    [property: JsonPropertyName("azure_connected")] bool? AzureConnected,
    [property: JsonPropertyName("message")] string? Message
);
