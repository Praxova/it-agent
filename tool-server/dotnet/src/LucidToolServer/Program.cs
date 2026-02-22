using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LucidToolServer.Configuration;
using LucidToolServer.Endpoints;
using LucidToolServer.Exceptions;
using LucidToolServer.Models.Requests;
using LucidToolServer.Models.Responses;
using LucidToolServer.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Enable running as a Windows Service
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "PraxovaToolServer";
});

// Configure Serilog (after UseWindowsService so content root is correct)
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

// Configure JSON options (camelCase)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

// Add configuration
builder.Services.Configure<ToolServerSettings>(
    builder.Configuration.GetSection("ToolServer"));

// Add services
builder.Services.AddScoped<IActiveDirectoryService, ActiveDirectoryService>();
builder.Services.AddScoped<IFilePermissionService, FilePermissionService>();
builder.Services.AddScoped<IRemoteManagementService, RemoteManagementService>();

// Azure service (conditional — only if configured)
var azureConfig = builder.Configuration.GetSection("ToolServer:Azure");
if (!string.IsNullOrEmpty(azureConfig["TenantId"]) && !string.IsNullOrEmpty(azureConfig["ClientId"]))
{
    builder.Services.AddScoped<IAzureService, AzureService>();
}

// HTTPS — configured programmatically (not via appsettings.json, which can't be
// cleanly disabled at runtime if cert files don't exist yet).
var httpsEnabled = false;
var certRelativePath = "certs/toolserver-cert.pem";
var keyRelativePath = "certs/toolserver-key.pem";
var fullCertPath = Path.Combine(builder.Environment.ContentRootPath, certRelativePath);
var fullKeyPath = Path.Combine(builder.Environment.ContentRootPath, keyRelativePath);

if (File.Exists(fullCertPath) && File.Exists(fullKeyPath))
{
    httpsEnabled = true;
    Log.Information("HTTPS enabled — loading cert from {CertPath}", fullCertPath);

    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.ListenAnyIP(8443, listenOptions =>
        {
            var pemCert = X509Certificate2.CreateFromPemFile(fullCertPath, fullKeyPath);
            // Re-export as PFX to persist the private key — CreateFromPemFile creates
            // an ephemeral key on Windows that SslStream/Kestrel cannot use for TLS.
            var cert = new X509Certificate2(pemCert.Export(X509ContentType.Pfx));
            listenOptions.UseHttps(cert);
        });
    });
}
else
{
    Log.Warning("HTTPS certificate files not found at {CertPath} / {KeyPath} — running HTTP only",
        fullCertPath, fullKeyPath);
}

var app = builder.Build();

// Lifecycle logging for service start/stop events
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

lifetime.ApplicationStarted.Register(() =>
    startupLogger.LogInformation("Praxova Tool Server started. Endpoints: {Urls}",
        string.Join(", ", app.Urls)));

lifetime.ApplicationStopping.Register(() =>
    startupLogger.LogInformation("Praxova Tool Server is shutting down..."));

lifetime.ApplicationStopped.Register(() =>
    startupLogger.LogInformation("Praxova Tool Server stopped."));

// HTTPS enforcement (only when cert files are present)
if (httpsEnabled)
{
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }
    app.UseHttpsRedirection();
}
else
{
    Log.Warning("HTTPS not configured — all traffic is unencrypted");
}

// Global exception handler
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled exception in request pipeline");

        context.Response.ContentType = "application/json";

        var (statusCode, errorCode) = ex switch
        {
            UserNotFoundException => (404, "UserNotFound"),
            GroupNotFoundException => (404, "GroupNotFound"),
            PathNotFoundException => (404, "PathNotFound"),
            PermissionDeniedException => (403, "PermissionDenied"),
            PathNotAllowedException => (403, "PathNotAllowed"),
            AdOperationException => (500, "OperationFailed"),
            _ => (500, "InternalError")
        };

        context.Response.StatusCode = statusCode;

        var errorResponse = new ErrorResponse(
            Error: errorCode,
            Message: ex.Message,
            Detail: ex.InnerException?.Message
        );

        await context.Response.WriteAsJsonAsync(errorResponse);
    }
});

// Map routes under /api/v1
var api = app.MapGroup("/api/v1");

// Health endpoint
api.MapGet("/health", async (IActiveDirectoryService adService, HttpContext httpContext) =>
{
    var adConnected = await adService.TestConnectionAsync();

    bool? azureConnected = null;
    var azureService = httpContext.RequestServices.GetService<IAzureService>();
    if (azureService != null)
    {
        azureConnected = await azureService.TestConnectionAsync();
    }

    var status = adConnected ? "healthy" : "unhealthy";
    var message = adConnected
        ? "Successfully connected to Active Directory"
        : "Failed to connect to Active Directory";

    if (azureConnected == true)
        message += ". Azure connected.";
    else if (azureConnected == false)
        message += ". Azure connection failed.";

    return new HealthResponse(
        Status: status,
        AdConnected: adConnected,
        AzureConnected: azureConnected,
        Message: message
    );
});

// Password reset endpoint
api.MapPost("/password/reset", async (
    PasswordResetRequest request,
    IActiveDirectoryService adService) =>
{
    if (string.IsNullOrWhiteSpace(request.Username))
    {
        return Results.BadRequest(new ErrorResponse(
            Error: "ValidationError",
            Message: "Username is required",
            Detail: null
        ));
    }

    // Generate secure temp password if none provided
    var password = string.IsNullOrWhiteSpace(request.NewPassword)
        ? GenerateSecurePassword()
        : request.NewPassword;

    var result = await adService.ResetPasswordAsync(request.Username, password);

    // Return the password in the response (either generated or provided)
    return Results.Ok(result with { TemporaryPassword = password });
});

// Group membership - add user
api.MapPost("/groups/add-member", async (
    GroupMembershipRequest request,
    IActiveDirectoryService adService) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.GroupName))
    {
        return Results.BadRequest(new ErrorResponse(
            Error: "ValidationError",
            Message: "Username and GroupName are required",
            Detail: null
        ));
    }

    var result = await adService.AddUserToGroupAsync(request.Username, request.GroupName);

    // Include ticket number in response
    return Results.Ok(result with { TicketNumber = request.TicketNumber });
});

// Group membership - remove user
api.MapPost("/groups/remove-member", async (
    GroupMembershipRequest request,
    IActiveDirectoryService adService) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.GroupName))
    {
        return Results.BadRequest(new ErrorResponse(
            Error: "ValidationError",
            Message: "Username and GroupName are required",
            Detail: null
        ));
    }

    var result = await adService.RemoveUserFromGroupAsync(request.Username, request.GroupName);

    // Include ticket number in response
    return Results.Ok(result with { TicketNumber = request.TicketNumber });
});

// Get group information
api.MapGet("/groups/{groupName}", async (
    string groupName,
    IActiveDirectoryService adService) =>
{
    var result = await adService.GetGroupAsync(groupName);
    return Results.Ok(result);
});

// Get user's groups
api.MapGet("/user/{username}/groups", async (
    string username,
    IActiveDirectoryService adService) =>
{
    var groups = await adService.GetUserGroupsAsync(username);

    return Results.Ok(new UserGroupsResponse(
        Success: true,
        Username: username,
        Groups: groups
    ));
});

// Search users (query endpoint for LLM)
api.MapGet("/user/search", async (
    string query,
    IActiveDirectoryService adService) =>
{
    var result = await adService.SearchUsersAsync(query);
    return Results.Ok(result);
});

// List groups (query endpoint for LLM)
api.MapGet("/groups", async (
    string? category,
    IActiveDirectoryService adService) =>
{
    var result = await adService.ListGroupsAsync(category);
    return Results.Ok(result);
});

// Search groups by keyword (query endpoint for LLM)
api.MapGet("/groups/search", async (
    string query,
    IActiveDirectoryService adService) =>
{
    if (string.IsNullOrWhiteSpace(query))
    {
        return Results.BadRequest(new ErrorResponse(
            Error: "InvalidQuery",
            Message: "Search query is required",
            Detail: null
        ));
    }

    var result = await adService.SearchGroupsAsync(query);
    return Results.Ok(result);
});

// Grant file permissions
api.MapPost("/permissions/grant", (
    FilePermissionRequest request,
    IFilePermissionService fileService) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.Permission))
    {
        return Results.BadRequest(new ErrorResponse(
            Error: "ValidationError",
            Message: "Username, Path, and Permission are required",
            Detail: null
        ));
    }

    var permissionLevel = request.Permission.ToLowerInvariant() switch
    {
        "read" => PermissionLevel.Read,
        "write" => PermissionLevel.Write,
        _ => throw new ArgumentException($"Invalid permission level: {request.Permission}")
    };

    fileService.GrantPermission(request.Path, request.Username, permissionLevel);

    return Results.Ok(new FilePermissionResponse(
        Success: true,
        Username: request.Username,
        Path: request.Path,
        Action: "granted",
        Permission: request.Permission,
        Message: $"{request.Permission} permission granted to {request.Username} on {request.Path}"
    ));
});

// Revoke file permissions
api.MapPost("/permissions/revoke", (
    FilePermissionRevokeRequest request,
    IFilePermissionService fileService) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Path))
    {
        return Results.BadRequest(new ErrorResponse(
            Error: "ValidationError",
            Message: "Username and Path are required",
            Detail: null
        ));
    }

    fileService.RevokePermission(request.Path, request.Username);

    return Results.Ok(new FilePermissionResponse(
        Success: true,
        Username: request.Username,
        Path: request.Path,
        Action: "revoked",
        Permission: null,
        Message: $"Permissions revoked for {request.Username} on {request.Path}"
    ));
});

// List file permissions
api.MapGet("/permissions/{*path}", (
    string path,
    IFilePermissionService fileService) =>
{
    var permissions = fileService.ListPermissions(path);

    return Results.Ok(new FilePermissionListResponse(
        Path: path,
        Permissions: permissions
    ));
});

// User Computer Lookup
api.MapGet("/user/{username}/computers", async (string username, IActiveDirectoryService adService) =>
{
    if (string.IsNullOrWhiteSpace(username))
        return Results.BadRequest(new ErrorResponse("ValidationError", "Username is required", null));

    var result = await adService.GetUserComputersAsync(username);
    return Results.Ok(result);
});

// Remote Software Install
api.MapPost("/software/install", async (SoftwareInstallRequest request, IRemoteManagementService remoteService) =>
{
    if (string.IsNullOrWhiteSpace(request.ComputerName))
        return Results.BadRequest(new ErrorResponse("ValidationError", "ComputerName is required", null));

    if (string.IsNullOrWhiteSpace(request.PackagePath))
        return Results.BadRequest(new ErrorResponse("ValidationError", "PackagePath is required", null));

    var result = await remoteService.InstallSoftwareAsync(
        request.ComputerName,
        request.PackagePath,
        request.Arguments,
        request.TicketNumber);

    return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
});

// Azure endpoints (only registered when Azure is configured)
if (!string.IsNullOrEmpty(azureConfig["TenantId"]) && !string.IsNullOrEmpty(azureConfig["ClientId"]))
{
    // Azure User Lookup
    api.MapGet("/azure/user/{upn}", async (string upn, IAzureService azureService) =>
    {
        if (string.IsNullOrWhiteSpace(upn))
            return Results.BadRequest(new ErrorResponse("ValidationError", "User principal name or ID is required", null));

        var result = await azureService.GetUserAsync(upn);
        return Results.Ok(result);
    });

    // Azure VM Lookup
    api.MapGet("/azure/vm/{vmName}", async (
        string vmName,
        string? resourceGroup,
        string? subscriptionId,
        IAzureService azureService) =>
    {
        if (string.IsNullOrWhiteSpace(vmName))
            return Results.BadRequest(new ErrorResponse("ValidationError", "VM name is required", null));

        var result = await azureService.GetVmAsync(vmName, resourceGroup, subscriptionId);
        return Results.Ok(result);
    });
}

// Map health check endpoints
app.MapHealthEndpoints();

app.Run();

/// <summary>
/// Generate a secure temporary password meeting AD complexity requirements.
/// Excludes ambiguous characters (I/l/1/0/O) for easier verbal communication.
/// </summary>
static string GenerateSecurePassword(int length = 16)
{
    const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // No I, O
    const string lower = "abcdefghjkmnpqrstuvwxyz";   // No i, l, o
    const string digits = "23456789";                  // No 0, 1
    const string special = "!@#$%&*?";

    var allChars = upper + lower + digits + special;
    var password = new char[length];

    var bytes = new byte[length];
    RandomNumberGenerator.Fill(bytes);

    // Ensure at least one of each category
    password[0] = upper[bytes[0] % upper.Length];
    password[1] = lower[bytes[1] % lower.Length];
    password[2] = digits[bytes[2] % digits.Length];
    password[3] = special[bytes[3] % special.Length];

    // Fill rest randomly from all chars
    for (int i = 4; i < length; i++)
        password[i] = allChars[bytes[i] % allChars.Length];

    // Shuffle using Fisher-Yates
    RandomNumberGenerator.Fill(bytes);
    for (int i = length - 1; i > 0; i--)
    {
        int j = bytes[i] % (i + 1);
        (password[i], password[j]) = (password[j], password[i]);
    }

    return new string(password);
}
