using System.DirectoryServices.Protocols;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LucidAdmin.Core.Interfaces.Credentials;
using LucidAdmin.Core.Interfaces.Repositories;
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

public record AdTestResult(
    bool Enabled,
    string Server,
    string Domain,
    bool Reachable,
    long LatencyMs,
    string? Error = null,
    LdapsStatus? Ldaps = null);

public record LdapsStatus(
    bool PortOpen,
    bool TlsHandshakeSuccess,
    bool CertTrusted,
    string? TlsError,
    TlsCertificateInfo? ServerCertificate);

public class AdSettingsService : IAdSettingsService
{
    private readonly IOptionsMonitor<ActiveDirectoryOptions> _adOptions;
    private readonly IConfigurationRoot _configurationRoot;
    private readonly ICredentialService _credentialService;
    private readonly IServiceAccountRepository _serviceAccountRepository;
    private readonly ITlsCertificateProbeService _tlsProbe;
    private readonly ILogger<AdSettingsService> _logger;

    public AdSettingsService(
        IOptionsMonitor<ActiveDirectoryOptions> adOptions,
        IConfiguration configuration,
        ICredentialService credentialService,
        IServiceAccountRepository serviceAccountRepository,
        ITlsCertificateProbeService tlsProbe,
        ILogger<AdSettingsService> logger)
    {
        _adOptions = adOptions;
        _configurationRoot = (IConfigurationRoot)configuration;
        _credentialService = credentialService;
        _serviceAccountRepository = serviceAccountRepository;
        _tlsProbe = tlsProbe;
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

        // Resolve bind credentials
        var (bindDn, bindPassword) = await ResolveBindCredentialsAsync(config);

        try
        {
            return await Task.Run(() =>
            {
                using var connection = new LdapConnection(
                    new LdapDirectoryIdentifier(config.LdapServer, config.LdapPort));

                connection.SessionOptions.ProtocolVersion = 3;
                if (config.UseLdaps)
                    connection.SessionOptions.SecureSocketLayer = true;

                BindConnection(connection, bindDn, bindPassword);

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

        // Resolve bind credentials
        var (bindDn, bindPassword) = await ResolveBindCredentialsAsync(config);

        bool reachable = false;
        long latencyMs = 0;
        string? error = null;

        // Step 1: Test the configured LDAP connection
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            reachable = await Task.Run(() =>
            {
                using var connection = new LdapConnection(
                    new LdapDirectoryIdentifier(config.LdapServer, config.LdapPort));

                connection.SessionOptions.ProtocolVersion = 3;
                if (config.UseLdaps)
                    connection.SessionOptions.SecureSocketLayer = true;

                if (!string.IsNullOrEmpty(bindDn))
                {
                    connection.AuthType = AuthType.Basic;
                    connection.Bind(new NetworkCredential(bindDn, bindPassword));
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
            latencyMs = sw.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AD connection test failed");
            error = DiagnoseLdapError(ex, config);
        }

        // Step 2: LDAPS probe — always probe port 636 to get cert info
        LdapsStatus? ldapsStatus = null;
        try
        {
            var probeResult = await _tlsProbe.ProbeCertificateAsync(config.LdapServer, 636);

            if (probeResult.Connected && probeResult.ServerCertificate != null)
            {
                // Determine if the cert is trusted by attempting a validated TLS connection
                var isTrusted = reachable && config.UseLdaps; // If LDAPS connection succeeded, cert is trusted

                ldapsStatus = new LdapsStatus(
                    PortOpen: true,
                    TlsHandshakeSuccess: probeResult.Error == null,
                    CertTrusted: isTrusted,
                    TlsError: probeResult.Error,
                    ServerCertificate: probeResult.ServerCertificate);
            }
            else if (probeResult.Connected)
            {
                ldapsStatus = new LdapsStatus(
                    PortOpen: true,
                    TlsHandshakeSuccess: false,
                    CertTrusted: false,
                    TlsError: probeResult.Error ?? "No certificate presented",
                    ServerCertificate: null);
            }
            else
            {
                ldapsStatus = new LdapsStatus(
                    PortOpen: false,
                    TlsHandshakeSuccess: false,
                    CertTrusted: false,
                    TlsError: probeResult.Error,
                    ServerCertificate: null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LDAPS probe failed for {Server}", config.LdapServer);
        }

        return new AdTestResult(true, config.LdapServer, config.Domain, reachable, latencyMs, error, ldapsStatus);
    }

    private static string DiagnoseLdapError(Exception ex, ActiveDirectoryOptions config)
    {
        var msg = ex.Message;
        var inner = ex.InnerException?.Message ?? "";
        var combined = $"{msg} {inner}".ToLowerInvariant();

        if (config.UseLdaps)
        {
            if (combined.Contains("remote certificate") || combined.Contains("certificate is invalid") ||
                combined.Contains("ssl") || combined.Contains("tls"))
                return "TLS certificate is not trusted. Import the DC certificate using the 'Trust This Certificate' button below.";

            if (combined.Contains("unavailable"))
                return "LDAPS connection failed — the server certificate may not be trusted, or port 636 is blocked.";
        }

        if (combined.Contains("refused"))
            return $"Connection refused on port {config.LdapPort} — verify the server address and port.";

        if (combined.Contains("timeout") || combined.Contains("timed out"))
            return $"Connection timed out — a firewall may be blocking port {config.LdapPort}.";

        if (combined.Contains("credential") || combined.Contains("password") || combined.Contains("bind"))
            return "Authentication failed — check the bind credentials.";

        return $"Connection failed: {msg}";
    }

    /// <summary>
    /// Resolves LDAP bind credentials. Prefers LdapServiceAccountId (credential service),
    /// falls back to legacy BindUserDn/BindPasswordEnvVar with deprecation warning.
    /// </summary>
    private async Task<(string? BindDn, string? Password)> ResolveBindCredentialsAsync(ActiveDirectoryOptions config)
    {
        // Preferred: ServiceAccount-based bind via credential service
        if (config.LdapServiceAccountId.HasValue)
        {
            var account = await _serviceAccountRepository.GetByIdAsync(config.LdapServiceAccountId.Value);
            if (account != null)
            {
                var credentials = await _credentialService.GetCredentialsAsync(account);
                if (credentials != null)
                {
                    // ServiceAccount Configuration JSON contains the bind DN
                    // Credential contains the password
                    var bindDn = account.Configuration != null
                        ? ExtractBindDnFromConfig(account.Configuration)
                        : null;
                    var password = credentials.Get("password");
                    return (bindDn, password);
                }
                _logger.LogWarning("LDAP ServiceAccount {AccountId} has no credentials stored", config.LdapServiceAccountId);
            }
            else
            {
                _logger.LogWarning("LDAP ServiceAccount {AccountId} not found", config.LdapServiceAccountId);
            }
        }

        // Legacy fallback: env var-based bind
        if (!string.IsNullOrEmpty(config.BindUserDn))
        {
            _logger.LogWarning(
                "Using legacy BindUserDn/BindPasswordEnvVar for LDAP bind — " +
                "configure LdapServiceAccountId in AD settings to use encrypted credential storage");
            var password = Environment.GetEnvironmentVariable(config.BindPasswordEnvVar) ?? "";
            return (config.BindUserDn, password);
        }

        return (null, null);
    }

    private static string? ExtractBindDnFromConfig(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("username", out var username))
                return username.GetString();
            if (doc.RootElement.TryGetProperty("bindDn", out var bindDn))
                return bindDn.GetString();
        }
        catch { }
        return null;
    }

    private static void BindConnection(LdapConnection connection, string? bindDn, string? password)
    {
        if (!string.IsNullOrEmpty(bindDn))
        {
            connection.AuthType = AuthType.Basic;
            connection.Bind(new NetworkCredential(bindDn, password));
        }
        else
        {
            connection.AuthType = AuthType.Anonymous;
            connection.Bind();
        }
    }

    private static string LdapEscape(string input) =>
        input.Replace("\\", "\\5c").Replace("*", "\\2a")
             .Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");
}
