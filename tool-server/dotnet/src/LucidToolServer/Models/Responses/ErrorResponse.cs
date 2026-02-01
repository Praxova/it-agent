using System.Text.Json.Serialization;

namespace LucidToolServer.Models.Responses;

/// <summary>
/// Error response returned when an exception occurs.
/// </summary>
public record ErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("detail")] string? Detail
);
