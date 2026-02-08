namespace LucidToolServer.Models.Requests;

/// <summary>
/// Request to install software on a remote computer.
/// </summary>
public record SoftwareInstallRequest(
    /// <summary>Target computer name or FQDN.</summary>
    string ComputerName,
    /// <summary>UNC path to the installer (.msi or .exe).</summary>
    string PackagePath,
    /// <summary>Optional installer arguments (defaults: /qn /norestart for MSI, /S for EXE).</summary>
    string? Arguments,
    /// <summary>Optional ticket number for audit trail.</summary>
    string? TicketNumber
);
