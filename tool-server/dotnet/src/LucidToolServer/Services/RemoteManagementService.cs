using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LucidToolServer.Configuration;
using LucidToolServer.Models.Responses;
using Microsoft.Extensions.Options;

namespace LucidToolServer.Services;

public class RemoteManagementService : IRemoteManagementService
{
    private readonly ToolServerSettings _settings;
    private readonly ILogger<RemoteManagementService> _logger;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    public RemoteManagementService(
        IOptions<ToolServerSettings> settings,
        ILogger<RemoteManagementService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SoftwareInstallResponse> InstallSoftwareAsync(
        string computerName,
        string packagePath,
        string? arguments = null,
        string? ticketNumber = null)
    {
        _logger.LogInformation(
            "Software install requested: Computer={Computer}, Package={Package}, Ticket={Ticket}",
            computerName, packagePath, ticketNumber);

        // Validate the package path is in an allowed location
        if (!IsPathAllowed(packagePath))
        {
            _logger.LogWarning("Package path not in allowed paths: {Path}", packagePath);
            return new SoftwareInstallResponse(
                Success: false,
                ComputerName: computerName,
                PackagePath: packagePath,
                ExitCode: -1,
                Output: null,
                ErrorOutput: $"Package path '{packagePath}' is not in the allowed software paths. Configure AllowedSoftwarePaths in settings.",
                Message: "Package path not allowed",
                TicketNumber: ticketNumber
            );
        }

        // Determine installer type and build the install command
        var extension = Path.GetExtension(packagePath).ToLowerInvariant();
        var installCommand = extension switch
        {
            ".msi" => BuildMsiCommand(packagePath, arguments),
            ".exe" => BuildExeCommand(packagePath, arguments),
            _ => null
        };

        if (installCommand == null)
        {
            return new SoftwareInstallResponse(
                Success: false,
                ComputerName: computerName,
                PackagePath: packagePath,
                ExitCode: -1,
                Output: null,
                ErrorOutput: $"Unsupported installer type: {extension}. Supported: .msi, .exe",
                Message: "Unsupported installer type",
                TicketNumber: ticketNumber
            );
        }

        // Build the full PowerShell Remoting command
        // Uses Invoke-Command to execute on the remote machine
        var psScript = $@"
            $ErrorActionPreference = 'Stop'
            try {{
                $result = Invoke-Command -ComputerName '{EscapePowerShell(computerName)}' -ScriptBlock {{
                    {installCommand}
                }} -ErrorAction Stop

                @{{
                    Success = $true
                    Output = $result | Out-String
                    ExitCode = $LASTEXITCODE
                }} | ConvertTo-Json
            }} catch {{
                @{{
                    Success = $false
                    Output = $_.Exception.Message
                    ExitCode = -1
                }} | ConvertTo-Json
            }}
        ";

        var (exitCode, stdout, stderr) = await RunPowerShellAsync(psScript);

        // Try to parse the JSON output from the script
        bool success = exitCode == 0;
        string? output = stdout;
        string? errorOutput = stderr;

        if (!string.IsNullOrEmpty(stdout))
        {
            try
            {
                var result = JsonSerializer.Deserialize<JsonElement>(stdout);
                success = result.GetProperty("Success").GetBoolean();
                output = result.GetProperty("Output").GetString()?.Trim();
                if (result.TryGetProperty("ExitCode", out var ec))
                    exitCode = ec.GetInt32();
            }
            catch
            {
                // If JSON parsing fails, use raw output
            }
        }

        var message = success
            ? $"Software installed successfully on {computerName}"
            : $"Software installation failed on {computerName}";

        _logger.LogInformation(
            "Software install {Result}: Computer={Computer}, ExitCode={ExitCode}, Ticket={Ticket}",
            success ? "succeeded" : "failed", computerName, exitCode, ticketNumber);

        return new SoftwareInstallResponse(
            Success: success,
            ComputerName: computerName,
            PackagePath: packagePath,
            ExitCode: exitCode,
            Output: output,
            ErrorOutput: string.IsNullOrWhiteSpace(errorOutput) ? null : errorOutput,
            Message: message,
            TicketNumber: ticketNumber
        );
    }

    public async Task<bool> TestConnectionAsync(string computerName)
    {
        var psScript = $@"
            try {{
                $result = Test-WSMan -ComputerName '{EscapePowerShell(computerName)}' -ErrorAction Stop
                'connected'
            }} catch {{
                'failed'
            }}
        ";

        var (exitCode, stdout, _) = await RunPowerShellAsync(psScript);
        return exitCode == 0 && stdout?.Trim() == "connected";
    }

    private static string BuildMsiCommand(string packagePath, string? arguments)
    {
        var args = string.IsNullOrWhiteSpace(arguments) ? "/qn /norestart" : arguments;
        return $@"
            $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList '/i ""{EscapePowerShell(packagePath)}"" {args}' -Wait -PassThru -NoNewWindow
            $LASTEXITCODE = $process.ExitCode
            if ($process.ExitCode -eq 0 -or $process.ExitCode -eq 3010) {{
                ""Installation completed (exit code: $($process.ExitCode))""
            }} else {{
                throw ""Installation failed with exit code: $($process.ExitCode)""
            }}
        ";
    }

    private static string BuildExeCommand(string packagePath, string? arguments)
    {
        var args = arguments ?? "/S";  // Common silent switch
        return $@"
            $process = Start-Process -FilePath '{EscapePowerShell(packagePath)}' -ArgumentList '{args}' -Wait -PassThru -NoNewWindow
            $LASTEXITCODE = $process.ExitCode
            if ($process.ExitCode -eq 0) {{
                ""Installation completed (exit code: $($process.ExitCode))""
            }} else {{
                throw ""Installation failed with exit code: $($process.ExitCode)""
            }}
        ";
    }

    private async Task<(int ExitCode, string? Stdout, string? Stderr)> RunPowerShellAsync(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit((int)DefaultTimeout.TotalMilliseconds));
        if (!completed)
        {
            process.Kill(true);
            return (-1, null, "Command timed out after 10 minutes");
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private bool IsPathAllowed(string packagePath)
    {
        if (_settings.AllowedSoftwarePaths == null || _settings.AllowedSoftwarePaths.Length == 0)
            return false;  // No paths configured = nothing allowed

        return _settings.AllowedSoftwarePaths.Any(allowed =>
        {
            // Simple wildcard matching (same pattern as AllowedPaths)
            if (allowed.Contains('*'))
            {
                var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(allowed)
                    .Replace("\\*", ".*") + "$";
                return System.Text.RegularExpressions.Regex.IsMatch(
                    packagePath, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            return packagePath.StartsWith(allowed, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''");
    }
}
