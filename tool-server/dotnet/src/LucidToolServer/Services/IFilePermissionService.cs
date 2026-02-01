using LucidToolServer.Models.Responses;

namespace LucidToolServer.Services;

/// <summary>
/// Service for file permission operations.
/// </summary>
public interface IFilePermissionService
{
    /// <summary>
    /// Grants file/folder permissions to a user.
    /// </summary>
    /// <param name="path">The UNC path.</param>
    /// <param name="username">The username (domain\username or username).</param>
    /// <param name="permission">The permission level (Read or Write).</param>
    void GrantPermission(string path, string username, PermissionLevel permission);

    /// <summary>
    /// Revokes file/folder permissions from a user.
    /// </summary>
    /// <param name="path">The UNC path.</param>
    /// <param name="username">The username (domain\username or username).</param>
    void RevokePermission(string path, string username);

    /// <summary>
    /// Lists all permissions on a file or folder.
    /// </summary>
    /// <param name="path">The UNC path.</param>
    /// <returns>List of permission entries.</returns>
    List<PermissionEntry> ListPermissions(string path);

    /// <summary>
    /// Performs a health check on the file permission service.
    /// </summary>
    /// <returns>True if healthy, false otherwise.</returns>
    bool HealthCheck();
}

/// <summary>
/// Permission levels for file access.
/// </summary>
public enum PermissionLevel
{
    Read,
    Write
}
