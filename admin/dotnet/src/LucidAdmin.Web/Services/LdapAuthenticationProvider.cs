using System.DirectoryServices.Protocols;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Core.Models;
using LucidAdmin.Web.Models;
using Microsoft.Extensions.Options;

namespace LucidAdmin.Web.Services;

public class LdapAuthenticationProvider : IAuthenticationProvider
{
    private readonly IOptions<ActiveDirectoryOptions> _options;
    private readonly ILogger<LdapAuthenticationProvider> _logger;

    public LdapAuthenticationProvider(
        IOptions<ActiveDirectoryOptions> options,
        ILogger<LdapAuthenticationProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool CanHandle(string username)
    {
        if (!_options.Value.Enabled) return false;

        return username.Contains('@') ||
               username.Contains('\\') ||
               _options.Value.Enabled;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password)
    {
        var config = _options.Value;
        var samAccountName = NormalizeUsername(username);

        return await Task.Run(() =>
        {
            try
            {
                // Step 1: LDAP bind with user credentials to verify password
                using var connection = new LdapConnection(
                    new LdapDirectoryIdentifier(config.LdapServer, config.LdapPort));

                connection.AuthType = AuthType.Basic;
                connection.SessionOptions.ProtocolVersion = 3;

                if (config.UseLdaps)
                {
                    connection.SessionOptions.SecureSocketLayer = true;
                }

                var bindDn = $"{samAccountName}@{config.Domain}";
                var credential = new System.Net.NetworkCredential(bindDn, password);
                connection.Bind(credential);

                // Step 2: Search for user to get attributes
                var searchRequest = new SearchRequest(
                    config.SearchBase,
                    $"(sAMAccountName={EscapeLdapFilter(samAccountName)})",
                    SearchScope.Subtree,
                    "displayName", "mail", "memberOf", "sAMAccountName");

                var searchResponse = (SearchResponse)connection.SendRequest(searchRequest);

                if (searchResponse.Entries.Count == 0)
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = "User not found in Active Directory"
                    };
                }

                var entry = searchResponse.Entries[0];
                var displayName = GetAttribute(entry, "displayName");
                var email = GetAttribute(entry, "mail");
                var memberOf = GetAttributes(entry, "memberOf");

                // Step 3: Resolve role from group membership
                var (role, matchedGroup) = ResolveRole(memberOf, config.RoleMapping, config.DefaultRole);

                if (!matchedGroup && config.RequireRoleGroup)
                {
                    _logger.LogWarning(
                        "AD user {Username} authenticated but is not a member of any LucidAdmin role group — denied by RequireRoleGroup policy",
                        samAccountName);
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = "Your AD account is not authorized for the Lucid Admin Portal. Contact your administrator to be added to a LucidAdmin group."
                    };
                }

                if (!matchedGroup)
                {
                    _logger.LogWarning(
                        "AD user {Username} is not a member of any LucidAdmin role group — assigned default role {Role}",
                        samAccountName, role);
                }

                _logger.LogInformation(
                    "AD authentication succeeded for {Username} (display: {DisplayName}, role: {Role})",
                    samAccountName, displayName, role);

                return new AuthenticationResult
                {
                    Success = true,
                    Username = samAccountName,
                    DisplayName = displayName ?? samAccountName,
                    Email = email ?? $"{samAccountName}@{config.Domain}",
                    Role = role,
                    AuthenticationMethod = "ActiveDirectory",
                    MustChangePassword = false
                };
            }
            catch (LdapException ex) when (ex.ErrorCode == 49)
            {
                _logger.LogWarning("AD authentication failed for {Username}: invalid credentials", samAccountName);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = "Invalid username or password"
                };
            }
            catch (LdapException ex)
            {
                _logger.LogError(ex, "LDAP error during authentication for {Username}", samAccountName);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = "Active Directory is unavailable. Use a local account."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during AD authentication for {Username}", samAccountName);
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = "Active Directory is unavailable. Use a local account."
                };
            }
        });
    }

    private static string NormalizeUsername(string username)
    {
        // user@domain.com → user
        if (username.Contains('@'))
            return username[..username.IndexOf('@')];

        // DOMAIN\user → user
        if (username.Contains('\\'))
            return username[(username.IndexOf('\\') + 1)..];

        return username;
    }

    private static (UserRole role, bool matchedGroup) ResolveRole(
        IReadOnlyList<string> memberOfDns,
        RoleMappingOptions mapping,
        string defaultRole)
    {
        // Check highest privilege first
        if (memberOfDns.Any(dn => dn.Contains($"CN={mapping.AdminGroup}", StringComparison.OrdinalIgnoreCase)))
            return (UserRole.Admin, true);

        if (memberOfDns.Any(dn => dn.Contains($"CN={mapping.OperatorGroup}", StringComparison.OrdinalIgnoreCase)))
            return (UserRole.Operator, true);

        if (memberOfDns.Any(dn => dn.Contains($"CN={mapping.ViewerGroup}", StringComparison.OrdinalIgnoreCase)))
            return (UserRole.Viewer, true);

        var role = Enum.TryParse<UserRole>(defaultRole, ignoreCase: true, out var parsed) ? parsed : UserRole.Viewer;
        return (role, false);
    }

    private static string? GetAttribute(SearchResultEntry entry, string attributeName)
    {
        if (!entry.Attributes.Contains(attributeName)) return null;
        var values = entry.Attributes[attributeName].GetValues(typeof(string));
        return values.Length > 0 ? (string)values[0] : null;
    }

    private static IReadOnlyList<string> GetAttributes(SearchResultEntry entry, string attributeName)
    {
        if (!entry.Attributes.Contains(attributeName)) return Array.Empty<string>();
        return entry.Attributes[attributeName]
            .GetValues(typeof(string))
            .Cast<string>()
            .ToList();
    }

    private static string EscapeLdapFilter(string input)
    {
        return input
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }
}
