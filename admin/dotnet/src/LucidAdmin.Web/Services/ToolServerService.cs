using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Exceptions;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;

namespace LucidAdmin.Web.Services;

public class ToolServerService : IToolServerService
{
    private readonly IToolServerRepository _repository;
    private readonly IAuditEventRepository _auditRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPasswordHasher _passwordHasher;

    public ToolServerService(
        IToolServerRepository repository,
        IAuditEventRepository auditRepository,
        IHttpClientFactory httpClientFactory,
        IPasswordHasher passwordHasher)
    {
        _repository = repository;
        _auditRepository = auditRepository;
        _httpClientFactory = httpClientFactory;
        _passwordHasher = passwordHasher;
    }

    public async Task<IEnumerable<ToolServer>> GetAllAsync(CancellationToken ct = default)
    {
        return await _repository.GetAllAsync(ct);
    }

    public async Task<ToolServer?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _repository.GetByIdAsync(id, ct);
    }

    public async Task<ToolServer> CreateAsync(ToolServer server, CancellationToken ct = default)
    {
        // Validate unique name
        var existingByName = await _repository.GetByNameAsync(server.Name, ct);
        if (existingByName != null)
        {
            throw new DuplicateEntityException("ToolServer", server.Name);
        }

        // Validate unique endpoint
        var existingByEndpoint = await _repository.GetByEndpointAsync(server.Endpoint, ct);
        if (existingByEndpoint != null)
        {
            throw new ValidationException("Endpoint", "Endpoint already registered");
        }

        // Initialize defaults
        server.Status = HealthStatus.Unknown;
        server.IsEnabled = true;

        await _repository.AddAsync(server, ct);

        // Audit
        await _auditRepository.AddAsync(new AuditEvent
        {
            ToolServerId = server.Id,
            Action = AuditAction.ToolServerRegistered,
            PerformedBy = "System",
            TargetResource = server.Name,
            Success = true
        }, ct);

        return server;
    }

    public async Task<ToolServer> UpdateAsync(ToolServer server, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(server.Id, ct);
        if (existing == null)
        {
            throw new EntityNotFoundException("ToolServer", server.Id);
        }

        await _repository.UpdateAsync(server, ct);

        // Audit
        await _auditRepository.AddAsync(new AuditEvent
        {
            ToolServerId = server.Id,
            Action = AuditAction.ToolServerRegistered,
            PerformedBy = "System",
            TargetResource = server.Name,
            Success = true
        }, ct);

        return server;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var server = await _repository.GetByIdAsync(id, ct);
        if (server == null)
        {
            throw new EntityNotFoundException("ToolServer", id);
        }

        var serverName = server.Name;

        // Audit BEFORE deleting (since ToolServerId FK will be set to null on delete due to SetNull behavior)
        await _auditRepository.AddAsync(new AuditEvent
        {
            ToolServerId = server.Id,
            Action = AuditAction.ToolServerDeregistered,
            PerformedBy = "System",
            TargetResource = serverName,
            Success = true
        }, ct);

        await _repository.DeleteAsync(id, ct);
    }

    public async Task<(HealthStatus Status, string Message, DateTime CheckedAt)> TestConnectivityAsync(Guid id, CancellationToken ct = default)
    {
        var server = await _repository.GetByIdAsync(id, ct);
        if (server == null)
        {
            throw new EntityNotFoundException("ToolServer", id);
        }

        HealthStatus status;
        string message;
        DateTime checkedAt = DateTime.UtcNow;

        try
        {
            var client = _httpClientFactory.CreateClient("ToolServer");
            client.Timeout = TimeSpan.FromSeconds(10);

            // Try to hit the tool server's health endpoint
            var healthUrl = $"{server.Endpoint.TrimEnd('/')}/api/v1/health";
            var response = await client.GetAsync(healthUrl, ct);

            if (response.IsSuccessStatusCode)
            {
                status = HealthStatus.Healthy;
                message = $"Successfully connected to {server.Endpoint}";
            }
            else
            {
                status = HealthStatus.Unhealthy;
                message = $"Tool server returned {response.StatusCode}";
            }
        }
        catch (HttpRequestException ex)
        {
            status = HealthStatus.Unhealthy;
            message = $"Connection failed: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            status = HealthStatus.Unhealthy;
            message = "Connection timed out after 10 seconds";
        }

        // Update server health status
        server.Status = status;
        server.LastHeartbeat = checkedAt;
        await _repository.UpdateAsync(server, ct);

        // Audit the test
        await _auditRepository.AddAsync(new AuditEvent
        {
            ToolServerId = server.Id,
            Action = AuditAction.ToolServerConnectivityTest,
            PerformedBy = "System",
            TargetResource = server.Name,
            Success = status == HealthStatus.Healthy,
            ErrorMessage = status != HealthStatus.Healthy ? message : null,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { action = "test-connectivity", result = status.ToString() })
        }, ct);

        return (status, message, checkedAt);
    }
}
