using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LucidToolServer.Configuration;
using Microsoft.Extensions.Options;

namespace LucidToolServer.Services;

/// <summary>
/// Fetches AD credentials from the Admin Portal's encrypted secrets store.
/// </summary>
public interface IPortalCredentialService
{
    /// <summary>
    /// Fetch AD credentials from the portal's encrypted secrets store.
    /// Returns (username, password) or null if unavailable.
    /// Refreshes the cache if expired.
    /// </summary>
    Task<(string Username, string Password)?> FetchAdCredentialsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Returns the last-fetched credentials from the in-memory cache.
    /// Does not make a network call — safe to call from synchronous code.
    /// Returns null if credentials have never been fetched or the cache has expired.
    /// </summary>
    (string Username, string Password)? CachedCredentials { get; }
}

/// <summary>
/// Implementation that calls the portal's ad-credentials endpoint
/// and caches the result in memory with a 15-minute TTL.
/// Registered as singleton so the cache persists across requests.
/// </summary>
public class PortalCredentialService : IPortalCredentialService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PortalSettings? _portalSettings;
    private readonly ILogger<PortalCredentialService> _logger;

    // In-memory cache
    private (string Username, string Password)? _cachedCredentials;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private bool _loggedFirstUse;

    internal const string HttpClientName = "PortalCredential";

    /// <inheritdoc />
    public (string Username, string Password)? CachedCredentials =>
        _cachedCredentials.HasValue && DateTime.UtcNow < _cacheExpiry
            ? _cachedCredentials
            : null;

    public PortalCredentialService(
        IHttpClientFactory httpClientFactory,
        IOptions<ToolServerSettings> settings,
        ILogger<PortalCredentialService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _portalSettings = settings.Value.Portal;
        _logger = logger;
    }

    public async Task<(string Username, string Password)?> FetchAdCredentialsAsync(
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_portalSettings?.Url) || string.IsNullOrEmpty(_portalSettings?.ToolServerId))
        {
            _logger.LogDebug("Portal URL or ToolServerId not configured — skipping credential fetch");
            return null;
        }

        // Return cached credentials if still valid
        if (_cachedCredentials.HasValue && DateTime.UtcNow < _cacheExpiry)
        {
            if (!_loggedFirstUse)
            {
                _logger.LogInformation("Using cached AD credentials from portal (user: {Username})",
                    _cachedCredentials.Value.Username);
                _loggedFirstUse = true;
            }
            else
            {
                _logger.LogDebug("Using cached AD credentials from portal (user: {Username})",
                    _cachedCredentials.Value.Username);
            }
            return _cachedCredentials;
        }

        // Reset first-use flag on cache refresh
        _loggedFirstUse = false;

        var url = $"{_portalSettings.Url.TrimEnd('/')}/api/tool-servers/{_portalSettings.ToolServerId}/ad-credentials";

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);

            if (!string.IsNullOrEmpty(_portalSettings.ApiKey))
                client.DefaultRequestHeaders.Add("X-API-Key", _portalSettings.ApiKey);

            var response = await client.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Portal credential fetch failed: {StatusCode} from {Url}",
                    response.StatusCode, url);
                return null;
            }

            var data = await response.Content.ReadFromJsonAsync<AdCredentialResponse>(ct);
            if (data == null || string.IsNullOrEmpty(data.Username) || string.IsNullOrEmpty(data.Password))
            {
                _logger.LogWarning("Portal returned empty credentials for tool server {Id}", _portalSettings.ToolServerId);
                return null;
            }

            // Cache the result
            _cachedCredentials = (data.Username, data.Password);
            _cacheExpiry = DateTime.UtcNow.Add(CacheTtl);

            _logger.LogInformation(
                "AD credentials loaded from portal secrets store for {Username} (cached for {Minutes}m)",
                data.Username, CacheTtl.TotalMinutes);

            return _cachedCredentials;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Portal credential fetch timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Portal credential fetch failed: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching credentials from portal");
            return null;
        }
    }

    private class AdCredentialResponse
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }
    }
}
