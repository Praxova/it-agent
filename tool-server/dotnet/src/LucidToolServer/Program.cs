using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
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

// Load Praxova CA for mTLS client certificate validation
X509Certificate2? praxovaCA = null;
var caRelativePath = "certs/ca.pem";  // Deployed by provision-toolserver-certs.ps1
var fullCaPath = Path.Combine(builder.Environment.ContentRootPath, caRelativePath);

if (File.Exists(fullCaPath))
{
    praxovaCA = new X509Certificate2(fullCaPath);
    Log.Information("Praxova CA loaded for mTLS — thumbprint: {Thumbprint}",
        Convert.ToHexString(praxovaCA.GetCertHash(HashAlgorithmName.SHA256)));
}
else
{
    Log.Warning("Praxova CA not found at {Path} — mTLS client cert validation disabled", fullCaPath);
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

            listenOptions.UseHttps(httpsOptions =>
            {
                httpsOptions.ServerCertificate = cert;
                // AllowCertificate = optional at TLS handshake; middleware enforces
                // requirement for operation endpoints (keeps health check simple)
                httpsOptions.ClientCertificateMode = ClientCertificateMode.AllowCertificate;

                if (praxovaCA != null)
                {
                    httpsOptions.ClientCertificateValidation = (clientCert, chain, errors) =>
                    {
                        using var customChain = new X509Chain();
                        customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        customChain.ChainPolicy.CustomTrustStore.Add(praxovaCA);
                        customChain.ChainPolicy.ApplicationPolicy.Add(
                            new Oid("1.3.6.1.5.5.7.3.2")); // id-kp-clientAuth
                        return customChain.Build(clientCert);
                    };
                }
            });
        });
    });
}
else
{
    Log.Warning("HTTPS certificate files not found at {CertPath} / {KeyPath} — running HTTP only",
        fullCertPath, fullKeyPath);
}

// Load operation token signing key
byte[]? operationTokenSigningKey = null;
var opTokenSettings = builder.Configuration.GetSection("ToolServer:OperationToken");
var signingKeyBase64 = opTokenSettings["SigningKeyBase64"];
var signingKeyPath = opTokenSettings["TokenSigningKeyPath"];

if (!string.IsNullOrEmpty(signingKeyBase64))
{
    operationTokenSigningKey = Convert.FromBase64String(signingKeyBase64);
    Log.Information("Operation token signing key loaded from configuration");
}
else if (!string.IsNullOrEmpty(signingKeyPath))
{
    var fullKeyPath2 = Path.IsPathRooted(signingKeyPath)
        ? signingKeyPath
        : Path.Combine(builder.Environment.ContentRootPath, signingKeyPath);

    if (File.Exists(fullKeyPath2))
    {
        var keyJson = File.ReadAllText(fullKeyPath2);
        var keyData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(keyJson);
        if (keyData?.TryGetValue("keyBase64", out var keyB64) == true)
        {
            operationTokenSigningKey = Convert.FromBase64String(keyB64);
            Log.Information("Operation token signing key loaded from file: {Path}", fullKeyPath2);
        }
    }
    else
    {
        Log.Warning("Operation token signing key file not found: {Path}. Token validation will be disabled.", fullKeyPath2);
    }
}
else
{
    Log.Warning("No operation token signing key configured. Token validation will be disabled. " +
        "Set ToolServer:OperationToken:SigningKeyBase64 or TokenSigningKeyPath.");
}

// Register OperationTokenValidator (only when signing key is available)
if (operationTokenSigningKey != null)
{
    var capMapJson = opTokenSettings.GetSection("CapabilityEndpointMap").Get<Dictionary<string, string[]>>()
        ?? new Dictionary<string, string[]>();
    var selfUrl = opTokenSettings["SelfUrl"] ?? "https://localhost:8443";
    var issuer = opTokenSettings["Issuer"] ?? "praxova-portal";

    builder.Services.AddSingleton(sp =>
        new OperationTokenValidator(
            operationTokenSigningKey,
            issuer,
            selfUrl,
            capMapJson,
            sp.GetRequiredService<ILogger<OperationTokenValidator>>()));
}

var app = builder.Build();

// Background nonce cleanup (only when validator is registered)
if (operationTokenSigningKey != null)
{
    var nonceCleanupTimer = new System.Threading.Timer(_ =>
    {
        try
        {
            var v = app.Services.GetService<OperationTokenValidator>();
            v?.CleanupExpiredNonces();
        }
        catch { }
    }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
}

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

// mTLS enforcement: require validated client cert for operation POSTs
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    var method = context.Request.Method;

    var requiresClientCert =
        praxovaCA != null &&
        method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
        path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase) &&
        !path.Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase);

    if (!requiresClientCert)
    {
        await next(context);
        return;
    }

    var clientCert = context.Connection.ClientCertificate;
    if (clientCert == null)
    {
        var mLogger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        mLogger.LogWarning("mTLS: No client cert for {Method} {Path}", method, path);

        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "client_cert_required",
            detail = "A valid Praxova agent client certificate is required"
        });
        return;
    }

    // Cert already validated by Kestrel's ClientCertificateValidation callback.
    // Log expiry warning if within 30 days.
    var daysLeft = (clientCert.NotAfter.ToUniversalTime() - DateTime.UtcNow).Days;
    if (daysLeft < 30)
    {
        var mLogger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        mLogger.LogWarning(
            "Agent client cert expires in {Days} days (CN={CN}, expires {Expiry}). " +
            "Agent will auto-renew on next startup.",
            daysLeft, clientCert.Subject, clientCert.NotAfter.ToUniversalTime());
    }

    await next(context);
});

// Operation token validation middleware
// Applies to all POST /api/v1/* endpoints except health.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    var method = context.Request.Method;

    // Require token: all POST requests to /api/v1/ except health
    var requiresToken =
        method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
        path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase) &&
        !path.Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase);

    if (!requiresToken)
    {
        await next(context);
        return;
    }

    // Get the validator (may be null if no signing key configured)
    var validator = context.RequestServices.GetService<OperationTokenValidator>();
    if (validator == null)
    {
        // Token validation not configured — log warning and allow (degraded mode)
        var degradedLogger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        degradedLogger.LogWarning(
            "Operation token validation not configured. Allowing request to {Path} without token validation.",
            path);
        await next(context);
        return;
    }

    // Extract Bearer token
    var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "token_missing",
            detail = "Authorization Bearer token required"
        });
        return;
    }

    var tokenString = authHeader["Bearer ".Length..].Trim();

    // Read request body for target validation (enable buffering so handler can read it too)
    context.Request.EnableBuffering();
    string? requestTarget = null;
    try
    {
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0; // Reset for actual handler

        if (!string.IsNullOrEmpty(body))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            // Try common target fields in order of specificity
            if (doc.RootElement.TryGetProperty("username", out var u))
                requestTarget = u.GetString();
            else if (doc.RootElement.TryGetProperty("path", out var p))
                requestTarget = p.GetString();
        }
    }
    catch
    {
        // If body parsing fails, proceed without target validation
    }

    var result = validator.Validate(tokenString, path, requestTarget);
    if (!result.IsValid)
    {
        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = result.ErrorCode,
            detail = result.ErrorDetail
        });
        return;
    }

    await next(context);
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
