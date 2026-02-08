namespace LucidToolServer.Models.Responses;

public record SoftwareInstallResponse(
    bool Success,
    string ComputerName,
    string PackagePath,
    int ExitCode,
    string? Output,
    string? ErrorOutput,
    string Message,
    string? TicketNumber
);
