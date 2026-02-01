using System.DirectoryServices.AccountManagement;
using LucidToolServer.Models.Requests;
using LucidToolServer.Models.Responses;

namespace LucidToolServer.Endpoints;

/// <summary>
/// Health check endpoints for testing service account connectivity.
/// These endpoints allow the Admin Portal to delegate health checks to the Tool Server.
/// </summary>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/health")
            .WithTags("Health");

        // POST /api/v1/health/test-connection
        // Tests connectivity to external systems (AD, LDAP, etc.)
        api.MapPost("/test-connection", async (TestConnectionRequest request, ILogger<Program> logger) =>
        {
            logger.LogInformation("Testing connection for provider type: {ProviderType}", request.ProviderType);

            try
            {
                return request.ProviderType.ToLower() switch
                {
                    "windows-ad" => await TestActiveDirectoryConnection(request, logger),
                    // Future: case "ldap", case "sql-server", etc.
                    _ => Results.BadRequest(new TestConnectionResponse(
                        Success: false,
                        Message: $"Unsupported provider type: {request.ProviderType}",
                        Details: null,
                        TestedAt: DateTime.UtcNow
                    ))
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Connection test failed for provider {ProviderType}", request.ProviderType);
                return Results.Ok(new TestConnectionResponse(
                    Success: false,
                    Message: "Connection test failed",
                    Details: ex.Message,
                    TestedAt: DateTime.UtcNow
                ));
            }
        });
    }

    private static async Task<IResult> TestActiveDirectoryConnection(
        TestConnectionRequest request,
        ILogger logger)
    {
        if (string.IsNullOrEmpty(request.Domain))
        {
            return Results.BadRequest(new TestConnectionResponse(
                Success: false,
                Message: "Domain is required for Active Directory connections",
                Details: null,
                TestedAt: DateTime.UtcNow
            ));
        }

        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Results.BadRequest(new TestConnectionResponse(
                Success: false,
                Message: "Username and password are required",
                Details: null,
                TestedAt: DateTime.UtcNow
            ));
        }

        try
        {
            logger.LogInformation(
                "Testing AD connection to domain {Domain} for user {Username}",
                request.Domain,
                request.Username);

            // Use a timeout to prevent hanging on slow AD responses (bad credentials, unreachable DCs)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            var connectedServer = await Task.Run(() =>
            {
                // Create context with domain only (no credentials) to avoid LDAP bind hang
                using var context = new PrincipalContext(
                    ContextType.Domain,
                    request.Domain
                );

                // ValidateCredentials performs the actual authentication test
                var isValid = context.ValidateCredentials(request.Username, request.Password);

                if (!isValid)
                {
                    throw new UnauthorizedAccessException("Invalid credentials");
                }

                logger.LogInformation(
                    "Successfully connected to domain {Domain} via DC {Server}",
                    request.Domain,
                    context.ConnectedServer);

                return context.ConnectedServer;
            }, cts.Token);

            // If we get here, credentials are valid
            return Results.Ok(new TestConnectionResponse(
                Success: true,
                Message: $"Successfully connected to domain {request.Domain}",
                Details: $"Connected to domain controller: {connectedServer}",
                TestedAt: DateTime.UtcNow
            ));
        }
        catch (PrincipalServerDownException ex)
        {
            logger.LogWarning(ex, "Cannot reach domain controller for {Domain}", request.Domain);
            return Results.Ok(new TestConnectionResponse(
                Success: false,
                Message: $"Cannot reach domain controller for {request.Domain}",
                Details: ex.Message,
                TestedAt: DateTime.UtcNow
            ));
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogWarning("Invalid credentials for user {Username} on domain {Domain}",
                request.Username, request.Domain);
            return Results.Ok(new TestConnectionResponse(
                Success: false,
                Message: "Invalid credentials",
                Details: "Username or password is incorrect",
                TestedAt: DateTime.UtcNow
            ));
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("AD connection test timed out for domain {Domain}", request.Domain);
            return Results.Ok(new TestConnectionResponse(
                Success: false,
                Message: "Connection test timed out",
                Details: "AD authentication took too long. This often indicates invalid credentials or network issues.",
                TestedAt: DateTime.UtcNow
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AD connection test failed for domain {Domain}", request.Domain);
            return Results.Ok(new TestConnectionResponse(
                Success: false,
                Message: $"AD connection test failed: {ex.Message}",
                Details: ex.Message,
                TestedAt: DateTime.UtcNow
            ));
        }
    }
}
