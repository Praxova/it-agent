using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Exceptions;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;

namespace LucidAdmin.Web.Services;

public class ServiceAccountService : IServiceAccountService
{
    private readonly IServiceAccountRepository _repository;
    private readonly IAuditEventRepository _auditRepository;
    private readonly IProviderRegistry _providerRegistry;

    public ServiceAccountService(
        IServiceAccountRepository repository,
        IAuditEventRepository auditRepository,
        IProviderRegistry providerRegistry)
    {
        _repository = repository;
        _auditRepository = auditRepository;
        _providerRegistry = providerRegistry;
    }

    public async Task<IEnumerable<ServiceAccount>> GetAllAsync(CancellationToken ct = default)
    {
        return await _repository.GetAllAsync(ct);
    }

    public async Task<ServiceAccount?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _repository.GetByIdAsync(id, ct);
    }

    public async Task<ServiceAccount> CreateAsync(ServiceAccount account, CancellationToken ct = default)
    {
        // Validate provider exists
        var provider = _providerRegistry.GetProvider(account.Provider);
        if (provider == null)
        {
            throw new InvalidOperationException($"Unknown provider: {account.Provider}");
        }

        // Only allow implemented providers
        if (!provider.IsImplemented)
        {
            throw new InvalidOperationException($"Provider '{account.Provider}' is not yet implemented");
        }

        // Validate configuration using provider
        var validation = provider.ValidateConfiguration(account.AccountType, account.Configuration);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Configuration validation failed: {string.Join(", ", validation.Errors)}");
        }

        // Check for duplicate name
        var existing = await _repository.GetByNameAsync(account.Name, ct);
        if (existing != null)
        {
            throw new DuplicateEntityException("ServiceAccount", account.Name);
        }

        account.HealthStatus = HealthStatus.Unknown;
        await _repository.AddAsync(account, ct);

        await _auditRepository.AddAsync(new AuditEvent
        {
            Action = AuditAction.ServiceAccountCreated,
            PerformedBy = "System",
            TargetResource = account.Name,
            Success = true
        }, ct);

        return account;
    }

    public async Task<ServiceAccount> UpdateAsync(ServiceAccount account, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(account.Id, ct);
        if (existing == null)
        {
            throw new EntityNotFoundException("ServiceAccount", account.Id);
        }

        // If configuration is being updated, validate it
        if (account.Configuration != null)
        {
            var provider = _providerRegistry.GetProvider(account.Provider);
            if (provider != null)
            {
                var validation = provider.ValidateConfiguration(account.AccountType, account.Configuration);
                if (!validation.IsValid)
                {
                    throw new InvalidOperationException($"Configuration validation failed: {string.Join(", ", validation.Errors)}");
                }
            }
        }

        await _repository.UpdateAsync(account, ct);

        await _auditRepository.AddAsync(new AuditEvent
        {
            Action = AuditAction.ServiceAccountUpdated,
            PerformedBy = "System",
            TargetResource = account.Name,
            Success = true
        }, ct);

        return account;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var account = await _repository.GetByIdAsync(id, ct);
        if (account == null)
        {
            throw new EntityNotFoundException("ServiceAccount", id);
        }

        await _repository.DeleteAsync(id, ct);

        await _auditRepository.AddAsync(new AuditEvent
        {
            Action = AuditAction.ServiceAccountDeleted,
            PerformedBy = "System",
            TargetResource = account.Name,
            Success = true
        }, ct);
    }

    public async Task<(HealthStatus Status, string Message, DateTime CheckedAt, Dictionary<string, object>? Details)> TestConnectivityAsync(Guid id, CancellationToken ct = default)
    {
        var account = await _repository.GetByIdAsync(id, ct);
        if (account == null)
        {
            throw new EntityNotFoundException("ServiceAccount", id);
        }

        var provider = _providerRegistry.GetProvider(account.Provider);
        if (provider == null)
        {
            throw new InvalidOperationException($"Unknown provider: {account.Provider}");
        }

        var result = await provider.TestConnectivityAsync(account);

        // Update account health status
        account.HealthStatus = result.Status;
        account.LastHealthCheck = result.CheckedAt;
        account.LastHealthMessage = result.Message;
        await _repository.UpdateAsync(account, ct);

        // Audit the test
        await _auditRepository.AddAsync(new AuditEvent
        {
            Action = AuditAction.ServiceAccountConnectivityTest,
            PerformedBy = "System",
            TargetResource = account.Name,
            Success = result.Status == HealthStatus.Healthy,
            ErrorMessage = result.Status != HealthStatus.Healthy ? result.Message : null,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { action = "test-connectivity", result = result.Status.ToString() })
        }, ct);

        return (result.Status, result.Message, result.CheckedAt, result.Details);
    }
}
