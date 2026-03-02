using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using LucidToolServer.Configuration;
using Microsoft.Extensions.Options;

namespace LucidToolServer.Services;

/// <summary>
/// Background service that periodically sends heartbeat signals to the admin portal
/// so the tool server is discoverable via capability routing.
/// </summary>
public class PortalHeartbeatService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PortalSettings _portalSettings;
    private readonly ILogger<PortalHeartbeatService> _logger;
    private readonly TimeSpan _interval;
    private bool _firstHeartbeatSent;

    internal const string HttpClientName = "PortalHeartbeat";

    public PortalHeartbeatService(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<ToolServerSettings> settings,
        ILogger<PortalHeartbeatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _portalSettings = settings.Value.Portal!;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(
            Math.Max(_portalSettings.HeartbeatIntervalSeconds, 10));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_portalSettings.Url) ||
            string.IsNullOrEmpty(_portalSettings.ToolServerId))
        {
            _logger.LogInformation(
                "Portal heartbeat disabled — Portal:Url or Portal:ToolServerId not configured");
            return;
        }

        _logger.LogInformation(
            "Portal heartbeat service starting (interval: {Interval}s, tool server: {Id})",
            (int)_interval.TotalSeconds, _portalSettings.ToolServerId);

        // Short delay to let other services initialize
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SendHeartbeatAsync(stoppingToken);

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Portal heartbeat service stopping");
    }

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        // Determine health status via AD connectivity check
        var status = await CheckAdHealthAsync(ct);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        var payload = new HeartbeatPayload
        {
            Version = version,
            Status = status,
            Hostname = Environment.MachineName,
            Capabilities = null
        };

        var url = $"{_portalSettings.Url.TrimEnd('/')}/api/tool-servers/{_portalSettings.ToolServerId}/heartbeat";

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);

            if (!string.IsNullOrEmpty(_portalSettings.ApiKey))
                client.DefaultRequestHeaders.Add("X-API-Key", _portalSettings.ApiKey);

            var response = await client.PostAsJsonAsync(url, payload, ct);

            if (response.IsSuccessStatusCode)
            {
                if (!_firstHeartbeatSent)
                {
                    _logger.LogInformation(
                        "Portal heartbeat established — tool server {Id} status: {Status}",
                        _portalSettings.ToolServerId, status);
                    _firstHeartbeatSent = true;
                }
                else
                {
                    _logger.LogDebug(
                        "Portal heartbeat sent — tool server {Id} status: {Status}",
                        _portalSettings.ToolServerId, status);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Portal heartbeat failed: {StatusCode} from {Url}",
                    response.StatusCode, url);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Clean shutdown — don't log as a warning
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Portal heartbeat timed out for {Url}", url);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Portal heartbeat failed: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error sending portal heartbeat");
        }
    }

    private async Task<string> CheckAdHealthAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var adService = scope.ServiceProvider.GetRequiredService<IActiveDirectoryService>();
            var connected = await adService.TestConnectionAsync();
            return connected ? "Healthy" : "Degraded";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AD connectivity check failed during heartbeat");
            return "Unhealthy";
        }
    }

    private class HeartbeatPayload
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; }

        [JsonPropertyName("capabilities")]
        public object? Capabilities { get; set; }
    }
}
