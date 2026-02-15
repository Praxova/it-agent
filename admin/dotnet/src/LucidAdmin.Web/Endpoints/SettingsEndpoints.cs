using System.DirectoryServices.Protocols;
using System.Text.Json;
using System.Text.Json.Serialization;
using LucidAdmin.Web.Authorization;
using LucidAdmin.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LucidAdmin.Web.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings")
            .WithTags("Settings")
            .RequireAuthorization(AuthorizationPolicies.RequireAdmin);

        group.MapGet("/active-directory", GetActiveDirectorySettings);
        group.MapPut("/active-directory", UpdateActiveDirectorySettings);
        group.MapGet("/active-directory/groups", SearchAdGroups);
    }

    private static IResult GetActiveDirectorySettings(
        IOptionsSnapshot<ActiveDirectoryOptions> adOptions)
    {
        return Results.Ok(adOptions.Value);
    }

    private static async Task<IResult> UpdateActiveDirectorySettings(
        ActiveDirectoryOptions settings,
        IConfiguration configuration,
        ILogger<ActiveDirectoryOptions> logger)
    {
        // Validate required fields when enabled
        if (settings.Enabled)
        {
            if (string.IsNullOrWhiteSpace(settings.Domain))
                return Results.BadRequest(new { error = "Domain is required when AD is enabled." });
            if (string.IsNullOrWhiteSpace(settings.LdapServer))
                return Results.BadRequest(new { error = "LDAP Server is required when AD is enabled." });
            if (string.IsNullOrWhiteSpace(settings.SearchBase))
                return Results.BadRequest(new { error = "Search Base is required when AD is enabled." });
        }

        if (settings.LdapPort < 1 || settings.LdapPort > 65535)
            return Results.BadRequest(new { error = "LDAP Port must be between 1 and 65535." });

        // Build the override file content with the section wrapper
        var wrapper = new Dictionary<string, object>
        {
            [ActiveDirectoryOptions.SectionName] = settings
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        var json = JsonSerializer.Serialize(wrapper, jsonOptions);

        // Write to the override file
        var dataDir = configuration.GetValue<string>("DataDirectory") ?? "/app/data";
        var filePath = Path.Combine(dataDir, "ad-settings.json");

        // Ensure directory exists
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(filePath, json);

        logger.LogInformation(
            "Active Directory settings saved to {Path}. Enabled={Enabled}, Domain={Domain}",
            filePath, settings.Enabled, settings.Domain);

        return Results.Ok(settings);
    }

    private record GroupSearchParams
    {
        [FromQuery(Name = "q")]
        public string? Q { get; init; }

        [FromQuery(Name = "maxResults")]
        public int MaxResults { get; init; } = 20;
    }

    private static async Task<IResult> SearchAdGroups(
        [AsParameters] GroupSearchParams search,
        IOptionsSnapshot<ActiveDirectoryOptions> adOptions,
        ILogger<ActiveDirectoryOptions> logger)
    {
        var config = adOptions.Value;

        if (!config.Enabled)
            return Results.Ok(Array.Empty<string>());

        var maxResults = Math.Min(Math.Max(search.MaxResults, 1), 50);

        try
        {
            var groups = await Task.Run(() =>
            {
                using var connection = new LdapConnection(
                    new LdapDirectoryIdentifier(config.LdapServer, config.LdapPort));

                connection.SessionOptions.ProtocolVersion = 3;

                if (config.UseLdaps)
                    connection.SessionOptions.SecureSocketLayer = true;

                // Bind with configured credentials or anonymous
                if (!string.IsNullOrEmpty(config.BindUserDn))
                {
                    var password = Environment.GetEnvironmentVariable(config.BindPasswordEnvVar) ?? "";
                    connection.AuthType = AuthType.Basic;
                    connection.Bind(new System.Net.NetworkCredential(config.BindUserDn, password));
                }
                else
                {
                    connection.AuthType = AuthType.Anonymous;
                    connection.Bind();
                }

                // Build LDAP filter
                string filter;
                if (string.IsNullOrWhiteSpace(search.Q))
                {
                    filter = "(objectCategory=group)";
                }
                else
                {
                    var escaped = LdapEscape(search.Q);
                    filter = $"(&(objectCategory=group)(cn=*{escaped}*))";
                }

                var searchRequest = new SearchRequest(
                    config.SearchBase,
                    filter,
                    SearchScope.Subtree,
                    "cn");

                searchRequest.SizeLimit = maxResults;

                var response = (SearchResponse)connection.SendRequest(searchRequest);

                var results = new List<string>();
                foreach (SearchResultEntry entry in response.Entries)
                {
                    if (entry.Attributes.Contains("cn"))
                    {
                        var cn = entry.Attributes["cn"].GetValues(typeof(string));
                        if (cn.Length > 0)
                            results.Add((string)cn[0]);
                    }
                }

                results.Sort(StringComparer.OrdinalIgnoreCase);
                return results;
            });

            return Results.Ok(groups);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AD group search failed for query '{Query}'", search.Q);
            return Results.Ok(Array.Empty<string>());
        }
    }

    private static string LdapEscape(string input)
    {
        return input
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }
}
