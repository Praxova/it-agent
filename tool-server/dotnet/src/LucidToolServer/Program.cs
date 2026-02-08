using System.Security.Cryptography;
using LucidToolServer.Configuration;
using LucidToolServer.Endpoints;
using LucidToolServer.Exceptions;
using LucidToolServer.Models.Requests;
using LucidToolServer.Models.Responses;
using LucidToolServer.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
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

var app = builder.Build();

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
api.MapGet("/health", async (IActiveDirectoryService adService) =>
{
    var adConnected = await adService.TestConnectionAsync();

    var status = adConnected ? "healthy" : "unhealthy";
    var message = adConnected
        ? "Successfully connected to Active Directory"
        : "Failed to connect to Active Directory";

    return new HealthResponse(
        Status: status,
        AdConnected: adConnected,
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
