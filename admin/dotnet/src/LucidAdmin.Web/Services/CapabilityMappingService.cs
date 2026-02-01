using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Exceptions;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Services;

public interface ICapabilityMappingService
{
    Task<IEnumerable<CapabilityMappingResponse>> GetAllAsync();
    Task<CapabilityMappingResponse?> GetByIdAsync(Guid id);
    Task<IEnumerable<CapabilityMappingResponse>> GetByToolServerIdAsync(Guid toolServerId);
    Task<CapabilityMappingResponse> CreateAsync(CreateCapabilityMappingRequest request);
    Task<CapabilityMappingResponse> UpdateAsync(Guid id, UpdateCapabilityMappingRequest request);
    Task DeleteAsync(Guid id);
}

public class CapabilityMappingService : ICapabilityMappingService
{
    private readonly ICapabilityMappingRepository _mappingRepository;
    private readonly IServiceAccountRepository _serviceAccountRepository;
    private readonly IToolServerRepository _toolServerRepository;
    private readonly ICapabilityRepository _capabilityRepository;

    public CapabilityMappingService(
        ICapabilityMappingRepository mappingRepository,
        IServiceAccountRepository serviceAccountRepository,
        IToolServerRepository toolServerRepository,
        ICapabilityRepository capabilityRepository)
    {
        _mappingRepository = mappingRepository;
        _serviceAccountRepository = serviceAccountRepository;
        _toolServerRepository = toolServerRepository;
        _capabilityRepository = capabilityRepository;
    }

    public async Task<IEnumerable<CapabilityMappingResponse>> GetAllAsync()
    {
        var mappings = await _mappingRepository.GetAllAsync();

        // Load navigation properties
        var mappingsWithNav = new List<CapabilityMapping>();
        foreach (var mapping in mappings)
        {
            var mappingWithNav = await LoadNavigationProperties(mapping);
            mappingsWithNav.Add(mappingWithNav);
        }

        return mappingsWithNav.Select(MapToResponse);
    }

    public async Task<CapabilityMappingResponse?> GetByIdAsync(Guid id)
    {
        var mapping = await _mappingRepository.GetByIdAsync(id);
        if (mapping == null)
        {
            return null;
        }

        mapping = await LoadNavigationProperties(mapping);
        return MapToResponse(mapping);
    }

    public async Task<IEnumerable<CapabilityMappingResponse>> GetByToolServerIdAsync(Guid toolServerId)
    {
        var mappings = await _mappingRepository.GetByToolServerIdAsync(toolServerId);

        // Load navigation properties
        var mappingsWithNav = new List<CapabilityMapping>();
        foreach (var mapping in mappings)
        {
            var mappingWithNav = await LoadNavigationProperties(mapping);
            mappingsWithNav.Add(mappingWithNav);
        }

        return mappingsWithNav.Select(MapToResponse);
    }

    public async Task<CapabilityMappingResponse> CreateAsync(CreateCapabilityMappingRequest request)
    {
        // Validate Service Account exists
        var serviceAccount = await _serviceAccountRepository.GetByIdAsync(request.ServiceAccountId);
        if (serviceAccount == null)
        {
            throw new EntityNotFoundException("ServiceAccount", request.ServiceAccountId);
        }

        // Validate Tool Server exists
        var toolServer = await _toolServerRepository.GetByIdAsync(request.ToolServerId);
        if (toolServer == null)
        {
            throw new EntityNotFoundException("ToolServer", request.ToolServerId);
        }

        // Validate Capability exists
        var cap = await _capabilityRepository.GetByIdAsync(request.CapabilityId);
        if (cap == null)
        {
            throw new InvalidOperationException($"Capability '{request.CapabilityId}' not found");
        }

        // Check for duplicate mapping (Tool Server + Capability)
        var existing = await _mappingRepository.GetByToolServerAndCapabilityAsync(
            request.ToolServerId, request.CapabilityId);
        if (existing != null)
        {
            throw new DuplicateEntityException(
                "CapabilityMapping",
                $"{toolServer.Name}/{request.CapabilityId}");
        }

        var mapping = new CapabilityMapping
        {
            ServiceAccountId = request.ServiceAccountId,
            ToolServerId = request.ToolServerId,
            CapabilityId = request.CapabilityId,
            CapabilityVersion = request.CapabilityVersion,
            Configuration = request.Configuration,
            AllowedScopesJson = request.AllowedScopesJson,
            DeniedScopesJson = request.DeniedScopesJson,
            IsEnabled = true,
            HealthStatus = HealthStatus.Unknown
        };

        await _mappingRepository.AddAsync(mapping);

        // Load navigation properties for response
        mapping.ServiceAccount = serviceAccount;
        mapping.ToolServer = toolServer;
        mapping.Capability = cap;

        return MapToResponse(mapping);
    }

    public async Task<CapabilityMappingResponse> UpdateAsync(Guid id, UpdateCapabilityMappingRequest request)
    {
        var mapping = await _mappingRepository.GetByIdAsync(id);
        if (mapping == null)
        {
            throw new EntityNotFoundException("CapabilityMapping", id);
        }

        // Update mutable fields
        if (request.CapabilityVersion != null)
        {
            mapping.CapabilityVersion = request.CapabilityVersion;
        }

        if (request.Configuration != null)
        {
            mapping.Configuration = request.Configuration;
        }

        if (request.AllowedScopesJson != null)
        {
            mapping.AllowedScopesJson = request.AllowedScopesJson;
        }

        if (request.DeniedScopesJson != null)
        {
            mapping.DeniedScopesJson = request.DeniedScopesJson;
        }

        if (request.IsEnabled.HasValue)
        {
            mapping.IsEnabled = request.IsEnabled.Value;
        }

        await _mappingRepository.UpdateAsync(mapping);

        // Load navigation properties for response
        mapping = await LoadNavigationProperties(mapping);

        return MapToResponse(mapping);
    }

    public async Task DeleteAsync(Guid id)
    {
        var mapping = await _mappingRepository.GetByIdAsync(id);
        if (mapping == null)
        {
            throw new EntityNotFoundException("CapabilityMapping", id);
        }

        await _mappingRepository.DeleteAsync(id);
    }

    private async Task<CapabilityMapping> LoadNavigationProperties(CapabilityMapping mapping)
    {
        // Load ServiceAccount
        if (mapping.ServiceAccount == null)
        {
            mapping.ServiceAccount = await _serviceAccountRepository.GetByIdAsync(mapping.ServiceAccountId);
        }

        // Load ToolServer
        if (mapping.ToolServer == null)
        {
            mapping.ToolServer = await _toolServerRepository.GetByIdAsync(mapping.ToolServerId);
        }

        // Load Capability
        if (mapping.Capability == null)
        {
            mapping.Capability = await _capabilityRepository.GetByIdAsync(mapping.CapabilityId);
        }

        return mapping;
    }

    private static CapabilityMappingResponse MapToResponse(CapabilityMapping mapping)
    {
        return new CapabilityMappingResponse(
            Id: mapping.Id,
            ServiceAccountId: mapping.ServiceAccountId,
            ToolServerId: mapping.ToolServerId,
            CapabilityId: mapping.CapabilityId,
            CapabilityVersion: mapping.CapabilityVersion,
            Configuration: mapping.Configuration,
            AllowedScopesJson: mapping.AllowedScopesJson,
            DeniedScopesJson: mapping.DeniedScopesJson,
            IsEnabled: mapping.IsEnabled,
            HealthStatus: mapping.HealthStatus.ToString(),
            LastHealthCheck: mapping.LastHealthCheck,
            LastHealthMessage: mapping.LastHealthMessage,
            CreatedAt: mapping.CreatedAt,
            UpdatedAt: mapping.UpdatedAt,
            // Display fields from navigation properties
            ServiceAccountName: mapping.ServiceAccount?.Name,
            ToolServerName: mapping.ToolServer?.Name,
            CapabilityDisplayName: mapping.Capability?.DisplayName,
            CapabilityCategory: mapping.Capability?.Category
        );
    }
}
