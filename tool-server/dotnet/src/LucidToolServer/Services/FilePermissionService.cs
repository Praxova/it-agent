using System.Security.AccessControl;
using System.Security.Principal;
using LucidToolServer.Configuration;
using LucidToolServer.Exceptions;
using LucidToolServer.Models.Responses;
using Microsoft.Extensions.Options;

namespace LucidToolServer.Services;

/// <summary>
/// File permission service implementation using System.Security.AccessControl.
/// </summary>
public class FilePermissionService : IFilePermissionService
{
    private readonly ToolServerSettings _settings;
    private readonly ILogger<FilePermissionService> _logger;

    public FilePermissionService(
        IOptions<ToolServerSettings> settings,
        ILogger<FilePermissionService> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public void GrantPermission(string path, string username, PermissionLevel permission)
    {
        _logger.LogInformation("Granting {Permission} permission to {Username} on {Path}", permission, username, path);

        ValidatePath(path);

        try
        {
            var directoryInfo = new DirectoryInfo(path);

            if (!directoryInfo.Exists)
            {
                throw new PathNotFoundException($"Path '{path}' does not exist");
            }

            var security = directoryInfo.GetAccessControl();

            // Determine rights based on permission level
            var rights = permission == PermissionLevel.Write
                ? FileSystemRights.Modify
                : FileSystemRights.Read;

            var rule = new FileSystemAccessRule(
                username,
                rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow
            );

            security.AddAccessRule(rule);
            directoryInfo.SetAccessControl(security);

            _logger.LogInformation("Successfully granted {Permission} permission to {Username} on {Path}", permission, username, path);
        }
        catch (Exception ex) when (ex is not PathNotFoundException && ex is not PathNotAllowedException)
        {
            _logger.LogError(ex, "Error granting permission to {Username} on {Path}", username, path);
            throw new AdOperationException($"Failed to grant permission: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public void RevokePermission(string path, string username)
    {
        _logger.LogInformation("Revoking permissions from {Username} on {Path}", username, path);

        ValidatePath(path);

        try
        {
            var directoryInfo = new DirectoryInfo(path);

            if (!directoryInfo.Exists)
            {
                throw new PathNotFoundException($"Path '{path}' does not exist");
            }

            var security = directoryInfo.GetAccessControl();

            // Remove all access rules for this user
            var rules = security.GetAccessRules(true, false, typeof(NTAccount))
                .Cast<FileSystemAccessRule>()
                .Where(r => r.IdentityReference.Value.Equals(username, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var rule in rules)
            {
                security.RemoveAccessRule(rule);
            }

            directoryInfo.SetAccessControl(security);

            _logger.LogInformation("Successfully revoked permissions from {Username} on {Path}", username, path);
        }
        catch (Exception ex) when (ex is not PathNotFoundException && ex is not PathNotAllowedException)
        {
            _logger.LogError(ex, "Error revoking permission from {Username} on {Path}", username, path);
            throw new AdOperationException($"Failed to revoke permission: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public List<PermissionEntry> ListPermissions(string path)
    {
        _logger.LogInformation("Listing permissions for {Path}", path);

        ValidatePath(path);

        try
        {
            var directoryInfo = new DirectoryInfo(path);

            if (!directoryInfo.Exists)
            {
                throw new PathNotFoundException($"Path '{path}' does not exist");
            }

            var security = directoryInfo.GetAccessControl();
            var rules = security.GetAccessRules(true, true, typeof(NTAccount));

            var permissions = new List<PermissionEntry>();

            foreach (FileSystemAccessRule rule in rules)
            {
                permissions.Add(new PermissionEntry(
                    Identity: rule.IdentityReference.Value,
                    AccessType: rule.AccessControlType.ToString(),
                    Rights: rule.FileSystemRights.ToString(),
                    IsInherited: rule.IsInherited
                ));
            }

            return permissions;
        }
        catch (Exception ex) when (ex is not PathNotFoundException && ex is not PathNotAllowedException)
        {
            _logger.LogError(ex, "Error listing permissions for {Path}", path);
            throw new AdOperationException($"Failed to list permissions: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public bool HealthCheck()
    {
        try
        {
            // Simple health check - verify we can access System.Security.AccessControl APIs
            _ = WindowsIdentity.GetCurrent();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File permission service health check failed");
            return false;
        }
    }

    private void ValidatePath(string path)
    {
        // If AllowedPaths is empty, all paths are allowed
        if (_settings.AllowedPaths.Length == 0)
        {
            return;
        }

        // Check if path matches any allowed patterns
        var isAllowed = _settings.AllowedPaths.Any(allowedPattern =>
        {
            // Convert wildcard pattern to regex
            var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(allowedPattern)
                .Replace(@"\*", ".*")
                .Replace(@"\?", ".") + "$";

            return System.Text.RegularExpressions.Regex.IsMatch(
                path,
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        });

        if (!isAllowed)
        {
            _logger.LogWarning("Path not allowed: {Path}", path);
            throw new PathNotAllowedException($"Path '{path}' is not in the allowed paths list");
        }
    }
}
