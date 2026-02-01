namespace LucidToolServer.Models.Responses;

/// <summary>
/// Response model for connection test results.
/// Returned by Tool Server to Admin Portal after testing connectivity.
/// </summary>
public record TestConnectionResponse(
    bool Success,
    string Message,
    string? Details,        // Additional info (e.g., DC name, response time, error details)
    DateTime TestedAt
);
