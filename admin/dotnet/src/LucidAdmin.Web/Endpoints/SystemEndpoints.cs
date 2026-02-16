using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Web.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/system")
            .WithTags("System");

        // Seal status — no auth required (UI needs to check before login is possible)
        group.MapGet("/seal-status", (ISealManager sealManager) =>
        {
            return Results.Ok(new SealStatusResponse(
                IsSealed: !sealManager.IsUnsealed,
                RequiresInitialization: sealManager.RequiresInitialization
            ));
        }).WithName("GetSealStatus");

        // Initialize — no auth required (only works on first-time setup)
        group.MapPost("/initialize", async (
            [FromBody] InitializeRequest request,
            ISealManager sealManager,
            IJwtKeyManager jwtKeyManager,
            ILogger<ISealManager> logger) =>
        {
            if (!sealManager.RequiresInitialization)
            {
                return Results.BadRequest(new { error = "Secrets store is already initialized. Use /unseal instead." });
            }

            if (string.IsNullOrWhiteSpace(request.Passphrase) || request.Passphrase.Length < 8)
            {
                return Results.BadRequest(new { error = "Passphrase must be at least 8 characters." });
            }

            try
            {
                await sealManager.InitializeAsync(request.Passphrase);

                // Initialize JWT key manager now that we're unsealed
                await jwtKeyManager.InitializeAsync();
                ConfigureJwtSigningKey(jwtKeyManager,
                    app.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<
                        Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>());

                logger.LogInformation("Secrets store initialized via API");
                return Results.Ok(new { message = "Secrets store initialized and unsealed." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize secrets store");
                return Results.Problem($"Initialization failed: {ex.Message}");
            }
        }).WithName("InitializeSecretsStore");

        // Unseal — no auth required (can't authenticate while sealed)
        // TODO: Add rate limiting to prevent brute force
        group.MapPost("/unseal", async (
            [FromBody] UnsealRequest request,
            ISealManager sealManager,
            IJwtKeyManager jwtKeyManager,
            ILogger<ISealManager> logger) =>
        {
            if (sealManager.IsUnsealed)
            {
                return Results.Ok(new { message = "Secrets store is already unsealed." });
            }

            if (string.IsNullOrWhiteSpace(request.Passphrase))
            {
                return Results.BadRequest(new { error = "Passphrase is required." });
            }

            var success = await sealManager.UnsealAsync(request.Passphrase);
            if (!success)
            {
                return Results.Unauthorized();
            }

            // Initialize JWT key manager now that we're unsealed
            try
            {
                await jwtKeyManager.InitializeAsync();
                ConfigureJwtSigningKey(jwtKeyManager,
                    app.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<
                        Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "JWT key manager initialization failed after unseal");
            }

            logger.LogInformation("Secrets store unsealed via API");
            return Results.Ok(new { message = "Secrets store unsealed." });
        }).WithName("UnsealSecretsStore");

        // Seal — admin only
        group.MapPost("/seal", (ISealManager sealManager, ILogger<ISealManager> logger) =>
        {
            sealManager.Seal();
            logger.LogInformation("Secrets store sealed via API");
            return Results.Ok(new { message = "Secrets store sealed. All credential operations are now unavailable." });
        })
        .WithName("SealSecretsStore")
        .RequireAuthorization(AuthorizationPolicies.RequireAdmin);
    }

    private static void ConfigureJwtSigningKey(
        IJwtKeyManager jwtKeyManager,
        Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions> optionsMonitor)
    {
        var jwtBearerOptions = optionsMonitor.Get(
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);
        jwtBearerOptions.TokenValidationParameters.IssuerSigningKey =
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(jwtKeyManager.GetSigningKey());
    }
}

// Request/Response DTOs
public record InitializeRequest(string Passphrase);
public record UnsealRequest(string Passphrase);
public record SealStatusResponse(bool IsSealed, bool RequiresInitialization);
