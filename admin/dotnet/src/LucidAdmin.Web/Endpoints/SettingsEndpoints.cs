using System.Text.Json;
using System.Text.Json.Serialization;
using LucidAdmin.Web.Authorization;
using LucidAdmin.Web.Models;
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
}
