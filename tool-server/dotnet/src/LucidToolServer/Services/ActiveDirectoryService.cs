using System.DirectoryServices.AccountManagement;
using LucidToolServer.Configuration;
using LucidToolServer.Exceptions;
using LucidToolServer.Models.Responses;
using Microsoft.Extensions.Options;

namespace LucidToolServer.Services;

/// <summary>
/// Active Directory service implementation using System.DirectoryServices.AccountManagement.
/// </summary>
public class ActiveDirectoryService : IActiveDirectoryService
{
    private readonly ToolServerSettings _settings;
    private readonly ILogger<ActiveDirectoryService> _logger;

    public ActiveDirectoryService(
        IOptions<ToolServerSettings> settings,
        ILogger<ActiveDirectoryService> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PasswordResetResponse> ResetPasswordAsync(string username, string newPassword)
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Attempting password reset for user: {Username}", username);

            // Check protected accounts
            if (_settings.ProtectedAccounts.Contains(username, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Attempted password reset on protected account: {Username}", username);
                throw new PermissionDeniedException($"Account '{username}' is protected and cannot be modified");
            }

            try
            {
                using var context = string.IsNullOrEmpty(_settings.DomainName)
                    ? new PrincipalContext(ContextType.Domain)
                    : new PrincipalContext(ContextType.Domain, _settings.DomainName);
                using var user = FindUserByMultipleStrategies(context, username);

                if (user == null)
                {
                    _logger.LogWarning("User not found: {Username}", username);
                    throw new UserNotFoundException($"User '{username}' not found in Active Directory");
                }

                // Reset password and force password change on next login
                user.SetPassword(newPassword);
                user.ExpirePasswordNow();
                user.Save();

                _logger.LogInformation("Password reset successful for user: {Username}", username);

                return new PasswordResetResponse(
                    Success: true,
                    Message: $"Password reset successfully for {username}",
                    Username: username,
                    UserDn: user.DistinguishedName
                );
            }
            catch (Exception ex) when (ex is not UserNotFoundException && ex is not PermissionDeniedException)
            {
                _logger.LogError(ex, "Error resetting password for user: {Username}", username);
                throw new AdOperationException($"Failed to reset password: {ex.Message}", ex);
            }
        });
    }

    /// <inheritdoc />
    public async Task<GroupMembershipResponse> AddUserToGroupAsync(string username, string groupName)
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Adding user {Username} to group {GroupName}", username, groupName);

            // Check protected groups
            if (_settings.ProtectedGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Attempted modification of protected group: {GroupName}", groupName);
                throw new PermissionDeniedException($"Group '{groupName}' is protected and cannot be modified");
            }

            try
            {
                using var context = string.IsNullOrEmpty(_settings.DomainName)
                    ? new PrincipalContext(ContextType.Domain)
                    : new PrincipalContext(ContextType.Domain, _settings.DomainName);
                using var user = FindUserByMultipleStrategies(context, username);

                if (user == null)
                {
                    _logger.LogWarning("User not found: {Username}", username);
                    throw new UserNotFoundException($"User '{username}' not found in Active Directory");
                }

                using var group = GroupPrincipal.FindByIdentity(context, IdentityType.SamAccountName, groupName);

                if (group == null)
                {
                    _logger.LogWarning("Group not found: {GroupName}", groupName);
                    throw new GroupNotFoundException($"Group '{groupName}' not found in Active Directory");
                }

                // Check if user is already a member
                if (group.Members.Contains(user))
                {
                    _logger.LogInformation("User {Username} is already a member of group {GroupName}", username, groupName);
                    return new GroupMembershipResponse(
                        Success: true,
                        Message: $"User {username} is already a member of {groupName}",
                        Username: username,
                        GroupName: groupName,
                        TicketNumber: ""
                    );
                }

                // Add user to group
                group.Members.Add(user);
                group.Save();

                _logger.LogInformation("Successfully added user {Username} to group {GroupName}", username, groupName);

                return new GroupMembershipResponse(
                    Success: true,
                    Message: $"User {username} added to {groupName}",
                    Username: username,
                    GroupName: groupName,
                    TicketNumber: ""
                );
            }
            catch (Exception ex) when (ex is not UserNotFoundException && ex is not GroupNotFoundException && ex is not PermissionDeniedException)
            {
                _logger.LogError(ex, "Error adding user {Username} to group {GroupName}", username, groupName);
                throw new AdOperationException($"Failed to add user to group: {ex.Message}", ex);
            }
        });
    }

    /// <inheritdoc />
    public async Task<GroupMembershipResponse> RemoveUserFromGroupAsync(string username, string groupName)
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Removing user {Username} from group {GroupName}", username, groupName);

            // Check protected groups
            if (_settings.ProtectedGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Attempted modification of protected group: {GroupName}", groupName);
                throw new PermissionDeniedException($"Group '{groupName}' is protected and cannot be modified");
            }

            try
            {
                using var context = string.IsNullOrEmpty(_settings.DomainName)
                    ? new PrincipalContext(ContextType.Domain)
                    : new PrincipalContext(ContextType.Domain, _settings.DomainName);
                using var user = FindUserByMultipleStrategies(context, username);

                if (user == null)
                {
                    _logger.LogWarning("User not found: {Username}", username);
                    throw new UserNotFoundException($"User '{username}' not found in Active Directory");
                }

                using var group = GroupPrincipal.FindByIdentity(context, IdentityType.SamAccountName, groupName);

                if (group == null)
                {
                    _logger.LogWarning("Group not found: {GroupName}", groupName);
                    throw new GroupNotFoundException($"Group '{groupName}' not found in Active Directory");
                }

                // Check if user is a member
                if (!group.Members.Contains(user))
                {
                    _logger.LogInformation("User {Username} is not a member of group {GroupName}", username, groupName);
                    return new GroupMembershipResponse(
                        Success: true,
                        Message: $"User {username} is not a member of {groupName}",
                        Username: username,
                        GroupName: groupName,
                        TicketNumber: ""
                    );
                }

                // Remove user from group
                group.Members.Remove(user);
                group.Save();

                _logger.LogInformation("Successfully removed user {Username} from group {GroupName}", username, groupName);

                return new GroupMembershipResponse(
                    Success: true,
                    Message: $"User {username} removed from {groupName}",
                    Username: username,
                    GroupName: groupName,
                    TicketNumber: ""
                );
            }
            catch (Exception ex) when (ex is not UserNotFoundException && ex is not GroupNotFoundException && ex is not PermissionDeniedException)
            {
                _logger.LogError(ex, "Error removing user {Username} from group {GroupName}", username, groupName);
                throw new AdOperationException($"Failed to remove user from group: {ex.Message}", ex);
            }
        });
    }

    /// <inheritdoc />
    public async Task<GroupInfoResponse> GetGroupAsync(string groupName)
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Getting information for group: {GroupName}", groupName);

            try
            {
                using var context = string.IsNullOrEmpty(_settings.DomainName)
                    ? new PrincipalContext(ContextType.Domain)
                    : new PrincipalContext(ContextType.Domain, _settings.DomainName);
                using var group = GroupPrincipal.FindByIdentity(context, IdentityType.SamAccountName, groupName);

                if (group == null)
                {
                    _logger.LogWarning("Group not found: {GroupName}", groupName);
                    throw new GroupNotFoundException($"Group '{groupName}' not found in Active Directory");
                }

                // Get member names
                var members = group.Members
                    .Cast<Principal>()
                    .Select(m => m.SamAccountName ?? m.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();

                return new GroupInfoResponse(
                    Success: true,
                    GroupName: group.SamAccountName ?? groupName,
                    GroupDn: group.DistinguishedName ?? "",
                    Description: group.Description,
                    Members: members!
                );
            }
            catch (Exception ex) when (ex is not GroupNotFoundException)
            {
                _logger.LogError(ex, "Error getting information for group: {GroupName}", groupName);
                throw new AdOperationException($"Failed to get group information: {ex.Message}", ex);
            }
        });
    }

    /// <inheritdoc />
    public async Task<List<string>> GetUserGroupsAsync(string username)
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Getting groups for user: {Username}", username);

            try
            {
                using var context = string.IsNullOrEmpty(_settings.DomainName)
                    ? new PrincipalContext(ContextType.Domain)
                    : new PrincipalContext(ContextType.Domain, _settings.DomainName);
                using var user = FindUserByMultipleStrategies(context, username);

                if (user == null)
                {
                    _logger.LogWarning("User not found: {Username}", username);
                    throw new UserNotFoundException($"User '{username}' not found in Active Directory");
                }

                // Get group names
                var groups = user.GetGroups()
                    .Cast<Principal>()
                    .Select(g => g.SamAccountName ?? g.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();

                return groups!;
            }
            catch (Exception ex) when (ex is not UserNotFoundException)
            {
                _logger.LogError(ex, "Error getting groups for user: {Username}", username);
                throw new AdOperationException($"Failed to get user groups: {ex.Message}", ex);
            }
        });
    }

    /// <inheritdoc />
    public async Task<bool> TestConnectionAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var context = string.IsNullOrEmpty(_settings.DomainName)
                    ? new PrincipalContext(ContextType.Domain)
                    : new PrincipalContext(ContextType.Domain, _settings.DomainName);
                // Try to get the computer name to test connectivity
                _ = context.ConnectedServer;
                _logger.LogInformation("Successfully connected to Active Directory");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to Active Directory");
                return false;
            }
        });
    }

    /// <inheritdoc />
    public async Task<UserSearchResponse> SearchUsersAsync(string query)
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Searching for users with query: {Query}", query);

            try
            {
                using var context = string.IsNullOrEmpty(_settings.DomainName)
                    ? new PrincipalContext(ContextType.Domain)
                    : new PrincipalContext(ContextType.Domain, _settings.DomainName);

                var results = new List<UserSearchResult>();

                // Use DirectorySearcher for more flexible LDAP filtering
                using var searcher = new PrincipalSearcher(new UserPrincipal(context));
                var allUsers = searcher.FindAll().OfType<UserPrincipal>();

                // Filter users based on query across multiple fields
                var matchedUsers = allUsers.Where(user =>
                {
                    var samAccountName = user.SamAccountName ?? "";
                    var displayName = user.DisplayName ?? "";
                    var email = user.EmailAddress ?? "";

                    // Get department and title from DirectoryEntry
                    string department = "";
                    string title = "";
                    try
                    {
                        if (user.GetUnderlyingObject() is System.DirectoryServices.DirectoryEntry entry)
                        {
                            department = entry.Properties["department"]?.Value?.ToString() ?? "";
                            title = entry.Properties["title"]?.Value?.ToString() ?? "";
                        }
                    }
                    catch
                    {
                        // Ignore errors accessing directory entry
                    }

                    // Search across all fields (case-insensitive)
                    return samAccountName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           displayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           email.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           department.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           title.Contains(query, StringComparison.OrdinalIgnoreCase);
                })
                .Take(50) // Limit results to prevent overwhelming responses
                .ToList();

                // Map to UserSearchResult
                foreach (var user in matchedUsers)
                {
                    string? department = null;
                    string? title = null;
                    try
                    {
                        if (user.GetUnderlyingObject() is System.DirectoryServices.DirectoryEntry entry)
                        {
                            department = entry.Properties["department"]?.Value?.ToString();
                            title = entry.Properties["title"]?.Value?.ToString();
                        }
                    }
                    catch
                    {
                        // Ignore errors
                    }

                    results.Add(new UserSearchResult(
                        SamAccountName: user.SamAccountName ?? "",
                        DisplayName: user.DisplayName,
                        Email: user.EmailAddress,
                        Department: department,
                        Title: title,
                        IsEnabled: user.Enabled ?? true
                    ));
                }

                _logger.LogInformation("Found {Count} users matching query: {Query}", results.Count, query);

                return new UserSearchResponse(
                    Success: true,
                    Query: query,
                    Results: results,
                    Count: results.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching for users with query: {Query}", query);
                throw new AdOperationException($"Failed to search users: {ex.Message}", ex);
            }
        });
    }

    /// <inheritdoc />
    public async Task<GroupListResponse> ListGroupsAsync(string? categoryFilter = null)
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Listing groups with category filter: {CategoryFilter}", categoryFilter ?? "none");

            try
            {
                using var context = string.IsNullOrEmpty(_settings.DomainName)
                    ? new PrincipalContext(ContextType.Domain)
                    : new PrincipalContext(ContextType.Domain, _settings.DomainName);

                var results = new List<GroupListItem>();

                // Search all groups
                using var searcher = new PrincipalSearcher(new GroupPrincipal(context));
                var allGroups = searcher.FindAll().OfType<GroupPrincipal>();

                foreach (var group in allGroups)
                {
                    var groupName = group.SamAccountName ?? group.Name;
                    if (string.IsNullOrEmpty(groupName))
                        continue;

                    // Extract category from name (prefix before hyphen)
                    string? category = null;
                    var hyphenIndex = groupName.IndexOf('-');
                    if (hyphenIndex > 0)
                    {
                        category = groupName.Substring(0, hyphenIndex);
                    }

                    // Apply category filter if specified
                    if (!string.IsNullOrEmpty(categoryFilter) &&
                        !string.Equals(category, categoryFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Get member count
                    int memberCount = 0;
                    try
                    {
                        memberCount = group.Members.Count;
                    }
                    catch
                    {
                        // Ignore errors getting member count
                    }

                    results.Add(new GroupListItem(
                        Name: groupName,
                        Description: group.Description,
                        Category: category,
                        MemberCount: memberCount
                    ));
                }

                _logger.LogInformation("Found {Count} groups with category filter: {CategoryFilter}", results.Count, categoryFilter ?? "none");

                return new GroupListResponse(
                    Success: true,
                    Groups: results,
                    Count: results.Count,
                    CategoryFilter: categoryFilter
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing groups with category filter: {CategoryFilter}", categoryFilter ?? "none");
                throw new AdOperationException($"Failed to list groups: {ex.Message}", ex);
            }
        });
    }

    /// <inheritdoc />
    public async Task<GroupListResponse> SearchGroupsAsync(string query)
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Searching groups with query: {Query}", query);

            try
            {
                using var context = string.IsNullOrEmpty(_settings.DomainName)
                    ? new PrincipalContext(ContextType.Domain)
                    : new PrincipalContext(ContextType.Domain, _settings.DomainName);

                var results = new List<GroupListItem>();

                // Search all groups
                using var searcher = new PrincipalSearcher(new GroupPrincipal(context));
                var allGroups = searcher.FindAll().OfType<GroupPrincipal>();

                foreach (var group in allGroups)
                {
                    var groupName = group.SamAccountName ?? group.Name;
                    if (string.IsNullOrEmpty(groupName))
                        continue;

                    var description = group.Description ?? "";

                    // Search in name OR description (case-insensitive)
                    if (!groupName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                        !description.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Extract category from name (prefix before hyphen)
                    string? category = null;
                    var hyphenIndex = groupName.IndexOf('-');
                    if (hyphenIndex > 0)
                    {
                        category = groupName.Substring(0, hyphenIndex);
                    }

                    // Get member count
                    int memberCount = 0;
                    try
                    {
                        memberCount = group.Members.Count;
                    }
                    catch
                    {
                        // Ignore errors getting member count
                    }

                    results.Add(new GroupListItem(
                        Name: groupName,
                        Description: group.Description,
                        Category: category,
                        MemberCount: memberCount
                    ));
                }

                _logger.LogInformation("Found {Count} groups matching query: {Query}", results.Count, query);

                return new GroupListResponse(
                    Success: true,
                    Groups: results,
                    Count: results.Count,
                    CategoryFilter: null  // Not a category filter, it's a search
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching groups with query: {Query}", query);
                throw new AdOperationException($"Failed to search groups: {ex.Message}", ex);
            }
        });
    }

    /// <summary>
    /// Find user using multiple search strategies for flexibility.
    /// Tries: SAMAccountName, DisplayName, GivenName+Surname, UserPrincipalName, and partial matches.
    /// </summary>
    /// <param name="context">Principal context to search in.</param>
    /// <param name="searchTerm">User identifier to search for.</param>
    /// <returns>Found UserPrincipal or null if not found.</returns>
    private UserPrincipal? FindUserByMultipleStrategies(PrincipalContext context, string searchTerm)
    {
        _logger.LogDebug("Searching for user with term: {SearchTerm}", searchTerm);

        // Strategy 1: Exact SAMAccountName match (e.g., "hsolo")
        var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, searchTerm);
        if (user != null)
        {
            _logger.LogDebug("Found user by SAMAccountName: {SamAccountName}", user.SamAccountName);
            return user;
        }

        // Strategy 2: Search by UserPrincipalName (e.g., "hsolo@montanifarms.com")
        user = UserPrincipal.FindByIdentity(context, IdentityType.UserPrincipalName, searchTerm);
        if (user != null)
        {
            _logger.LogDebug("Found user by UserPrincipalName: {UserPrincipalName}", user.UserPrincipalName);
            return user;
        }

        // Strategy 3: Search by DisplayName (e.g., "Han Solo")
        using (var searcher = new PrincipalSearcher(new UserPrincipal(context) { DisplayName = searchTerm }))
        {
            user = searcher.FindOne() as UserPrincipal;
            if (user != null)
            {
                _logger.LogDebug("Found user by DisplayName: {DisplayName}", user.DisplayName);
                return user;
            }
        }

        // Strategy 4: Try to parse as "FirstName LastName" and search by GivenName + Surname
        var nameParts = searchTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (nameParts.Length >= 2)
        {
            var givenName = nameParts[0];
            var surname = string.Join(" ", nameParts.Skip(1)); // Handle multi-part last names

            using var searcher = new PrincipalSearcher(new UserPrincipal(context)
            {
                GivenName = givenName,
                Surname = surname
            });
            user = searcher.FindOne() as UserPrincipal;
            if (user != null)
            {
                _logger.LogDebug("Found user by GivenName+Surname: {GivenName} {Surname}", givenName, surname);
                return user;
            }
        }

        // Strategy 5: Partial/fuzzy match on DisplayName
        var partialMatches = new List<UserPrincipal>();
        using (var searcher = new PrincipalSearcher(new UserPrincipal(context)))
        {
            var results = searcher.FindAll()
                .OfType<UserPrincipal>()
                .Where(u => u.DisplayName != null &&
                           u.DisplayName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();

            if (results.Count == 1)
            {
                _logger.LogDebug("Found user by partial DisplayName match: {DisplayName}", results[0].DisplayName);
                return results[0];
            }
            else if (results.Count > 1)
            {
                partialMatches = results;
                _logger.LogDebug("Found {Count} partial matches for: {SearchTerm}", results.Count, searchTerm);
            }
        }

        // No exact match found - provide helpful suggestions if we have partial matches
        if (partialMatches.Any())
        {
            var suggestions = partialMatches
                .Select(u => u.SamAccountName ?? u.DisplayName)
                .Where(s => !string.IsNullOrEmpty(s))
                .Take(3);

            var suggestionList = string.Join(", ", suggestions);
            _logger.LogWarning(
                "User '{SearchTerm}' not found. Tried: SAMAccountName, DisplayName, GivenName+Surname, UserPrincipalName. " +
                "Did you mean one of: {Suggestions}?",
                searchTerm,
                suggestionList
            );

            throw new UserNotFoundException(
                $"User '{searchTerm}' not found. Tried: SAMAccountName, DisplayName, Name. " +
                $"Did you mean one of: {suggestionList}?"
            );
        }

        // No matches at all
        _logger.LogWarning("User '{SearchTerm}' not found after trying all search strategies", searchTerm);
        return null;
    }
}
