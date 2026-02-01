using LucidToolServer.Models.Responses;

namespace LucidToolServer.Services;

/// <summary>
/// Service for Active Directory operations.
/// </summary>
public interface IActiveDirectoryService
{
    /// <summary>
    /// Resets a user's password in Active Directory.
    /// </summary>
    /// <param name="username">The username (sAMAccountName).</param>
    /// <param name="newPassword">The new password.</param>
    /// <returns>Password reset response.</returns>
    Task<PasswordResetResponse> ResetPasswordAsync(string username, string newPassword);

    /// <summary>
    /// Adds a user to an Active Directory group.
    /// </summary>
    /// <param name="username">The username (sAMAccountName).</param>
    /// <param name="groupName">The group name.</param>
    /// <returns>Group membership response.</returns>
    Task<GroupMembershipResponse> AddUserToGroupAsync(string username, string groupName);

    /// <summary>
    /// Removes a user from an Active Directory group.
    /// </summary>
    /// <param name="username">The username (sAMAccountName).</param>
    /// <param name="groupName">The group name.</param>
    /// <returns>Group membership response.</returns>
    Task<GroupMembershipResponse> RemoveUserFromGroupAsync(string username, string groupName);

    /// <summary>
    /// Gets information about an Active Directory group.
    /// </summary>
    /// <param name="groupName">The group name.</param>
    /// <returns>Group information response.</returns>
    Task<GroupInfoResponse> GetGroupAsync(string groupName);

    /// <summary>
    /// Gets a list of groups a user belongs to.
    /// </summary>
    /// <param name="username">The username (sAMAccountName).</param>
    /// <returns>List of group names.</returns>
    Task<List<string>> GetUserGroupsAsync(string username);

    /// <summary>
    /// Tests the connection to Active Directory.
    /// </summary>
    /// <returns>True if connection is successful, false otherwise.</returns>
    Task<bool> TestConnectionAsync();

    /// <summary>
    /// Searches for users in Active Directory by query string.
    /// Searches across DisplayName, SamAccountName, Email, Department, and Title fields.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <returns>User search response with matching results.</returns>
    Task<UserSearchResponse> SearchUsersAsync(string query);

    /// <summary>
    /// Lists all groups in Active Directory, optionally filtered by category.
    /// Category is extracted from group name prefix (e.g., "DEPT-" → "DEPT").
    /// </summary>
    /// <param name="categoryFilter">Optional category filter (prefix before hyphen).</param>
    /// <returns>Group list response.</returns>
    Task<GroupListResponse> ListGroupsAsync(string? categoryFilter = null);

    /// <summary>
    /// Searches for groups in Active Directory by query string.
    /// Searches across group name and description fields.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <returns>Group list response with matching results.</returns>
    Task<GroupListResponse> SearchGroupsAsync(string query);
}
