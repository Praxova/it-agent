using LucidToolServer.Models.Responses;

namespace LucidToolServer.Services;

/// <summary>
/// Service for remote management operations via PowerShell Remoting (WinRM).
/// </summary>
public interface IRemoteManagementService
{
    /// <summary>
    /// Install software on a remote computer using PowerShell Remoting.
    /// Copies the installer from a UNC path and executes it on the target.
    /// </summary>
    /// <param name="computerName">Target computer name or FQDN.</param>
    /// <param name="packagePath">UNC path to the installer (MSI, EXE, etc.).</param>
    /// <param name="arguments">Optional installer arguments.</param>
    /// <param name="ticketNumber">Optional ticket reference for audit trail.</param>
    /// <returns>Install result.</returns>
    Task<SoftwareInstallResponse> InstallSoftwareAsync(
        string computerName,
        string packagePath,
        string? arguments = null,
        string? ticketNumber = null);

    /// <summary>
    /// Test WinRM connectivity to a remote computer.
    /// </summary>
    Task<bool> TestConnectionAsync(string computerName);
}
