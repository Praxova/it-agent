namespace LucidToolServer.Models.Responses;

public record UserComputersResponse(
    bool Success,
    string Username,
    string DisplayName,
    List<ComputerInfo> Computers,
    int Count,
    string Message
);

public record ComputerInfo(
    string Name,
    string? DnsHostName,
    string? OperatingSystem,
    string? OperatingSystemVersion,
    string? LastLogon,
    string? Description,
    string DistinguishedName
);
