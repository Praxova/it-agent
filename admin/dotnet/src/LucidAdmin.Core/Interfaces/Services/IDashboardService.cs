using LucidAdmin.Core.Models;

namespace LucidAdmin.Core.Interfaces.Services;

public interface IDashboardService
{
    Task<DashboardStats> GetStatsAsync(int recentAuditCount = 10, CancellationToken ct = default);
}
