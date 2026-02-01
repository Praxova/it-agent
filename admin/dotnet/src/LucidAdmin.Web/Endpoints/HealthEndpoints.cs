using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Web.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/health").WithTags("Health");

        group.MapGet("/", async (LucidDbContext context) =>
        {
            var canConnect = await context.Database.CanConnectAsync();
            var status = canConnect ? "healthy" : "unhealthy";

            return Results.Ok(new HealthResponse(
                Status: status,
                Timestamp: DateTime.UtcNow,
                Version: "1.0.0",
                Database: canConnect ? "connected" : "disconnected"
            ));
        });
    }
}
