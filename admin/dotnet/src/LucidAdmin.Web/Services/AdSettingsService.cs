using System.DirectoryServices.Protocols;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LucidAdmin.Web.Models;
using Microsoft.Extensions.Options;

namespace LucidAdmin.Web.Services;

public interface IAdSettingsService
{
    ActiveDirectoryOptions GetCurrentSettings();
    Task<(bool Success, string? Error)> SaveSettingsAsync(ActiveDirectoryOptions settings);
    Task<IReadOnlyList<string>> SearchGroupsAsync(string? query, int maxResults = 20);
    Task<AdTestResult> TestConnectionAsync();
}

public record AdTestResult(bool Enabled, string Server, string Domain, bool Reachable, long LatencyMs);

public class AdSettingsService : IAdSettingsService
{
    private readonly IOptionsMonitor<ActiveDirectoryOptions> _adOptions;
    private readonly IConfigurationRoot _configurationRoot;
    private readonly ILogger<AdSettingsService> _logger;

    public AdSettingsService(
        IOptionsMonitor<ActiveDirectoryOptions> adOptions,
        IConfiguration configuration,
        ILogger<AdSettingsService> logger)
    {
        _adOptions = adOptions;
        _configurationRoot = (IConfigurationRoot)configuration;
        _logger = logger;
    }

    public ActiveDirectoryOptions GetCurrentSettings() => _adOptions.CurrentValue;

    public async Task<(bool Success, string? Error)> SaveSettingsAsync(ActiveDirectoryOptions settings)
    {
        if (settings.Enabled)
        {
            if (string.IsNullOrWhiteSpace(settings.Domain))
                return (false, "Domain is required when AD is enabled.");
            if (string.IsNullOrWhiteSpace(settings.LdapServer))
                return (false, "LDAP Server is required when AD is enabled.");
            if (string.IsNullOrWhiteSpace(settings.SearchBase))
                return (false, "Search Base is required when AD is enabled.");
        }
        if (settings.LdapPort < 1 || settings.LdapPort > 65535)
            return (false, "LDAP Port must be between 1 and 65535.");

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

        var dataDir = _configurationRoot.GetValue<string>("DataDirectory") ?? "/app/data";
        var filePath = Path.Combine(dataDir, "ad-settings.json");

        var dir = Path.GetDirectoryName(filePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(filePath, json);

        // Force reload — Docker volumes don't trigger inotify for reloadOnChange
        _configurationRoot.Reload();

        _logger.LogInformation("AD settings saved to {Path}. Enabled={Enabled}, Domain={Domain}",
            filePath, settings.Enabled, settings.Domain);

        return (true, null);
    }

    public async Task<IReadOnlyList<string>> SearchGroupsAsync(string? query, int maxResults = 20)
    {
        var config = _adOptions.CurrentValue;
        if (!config.Enabled)
            return Array.Empty<string>();

        maxResults = Math.Min(Math.Max(maxResults, 1), 50);

        try
        {
            return await Task.Run(() =>
            {
                using var connection = new LdapConnection(
                    new LdapDirectoryIdentifier(config.LdapServer, config.LdapPort));

                connection.SessionOptions.ProtocolVersion = 3;
                if (config.UseLdaps)
                    connection.SessionOptions.SecureSocketLayer = true;

                if (!string.IsNullOrEmpty(config.BindUserDn))
                {
                    var password = Environment.GetEnvironmentVariable(config.BindPasswordEnvVar) ?? "";
                    connection.AuthType = AuthType.Basic;
                    connection.Bind(new NetworkCredential(config.BindUserDn, password));
                }
                else
                {
                    connection.AuthType = AuthType.Anonymous;
                    connection.Bind();
                }

                string filter;
                if (string.IsNullOrWhiteSpace(query))
                    filter = "(objectCategory=group)";
                else
                {
                    var escaped = LdapEscape(query);
                    filter = $"(&(objectCategory=group)(cn=*{escaped}*))";
                }

                var searchRequest = new SearchRequest(config.SearchBase, filter, SearchScope.Subtree, "cn");
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
                return (IReadOnlyList<string>)results;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AD group search failed for query '{Query}'", query);
            return Array.Empty<string>();
        }
    }

    public async Task<AdTestResult> TestConnectionAsync()
    {
        var config = _adOptions.CurrentValue;
        if (!config.Enabled)
            return new AdTestResult(false, "", "", false, 0);

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var reachable = await Task.Run(() =>
            {
                using var connection = new LdapConnection(
                    new LdapDirectoryIdentifier(config.LdapServer, config.LdapPort));

                connection.SessionOptions.ProtocolVersion = 3;
                if (config.UseLdaps)
                    connection.SessionOptions.SecureSocketLayer = true;

                if (!string.IsNullOrEmpty(config.BindUserDn))
                {
                    var password = Environment.GetEnvironmentVariable(config.BindPasswordEnvVar) ?? "";
                    connection.AuthType = AuthType.Basic;
                    connection.Bind(new NetworkCredential(config.BindUserDn, password));
                }
                else
                {
                    connection.AuthType = AuthType.Anonymous;
                    var searchRequest = new SearchRequest(
                        "", "(objectClass=*)", SearchScope.Base, "defaultNamingContext");
                    connection.SendRequest(searchRequest);
                }

                return true;
            });
            sw.Stop();

            return new AdTestResult(true, config.LdapServer, config.Domain, reachable, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AD connection test failed");
            return new AdTestResult(true, config.LdapServer, config.Domain, false, 0);
        }
    }

    private static string LdapEscape(string input) =>
        input.Replace("\\", "\\5c").Replace("*", "\\2a")
             .Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");
}
