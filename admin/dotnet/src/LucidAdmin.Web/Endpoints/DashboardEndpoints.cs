using LucidAdmin.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization();

        // GET /api/v1/dashboard/stats - Get dashboard statistics
        group.MapGet("/stats", async (
            IDashboardService dashboardService,
            [FromQuery] int recentAuditCount = 10,
            CancellationToken ct = default) =>
        {
            var stats = await dashboardService.GetStatsAsync(recentAuditCount, ct);
            return Results.Ok(stats);
        });
    }
}
