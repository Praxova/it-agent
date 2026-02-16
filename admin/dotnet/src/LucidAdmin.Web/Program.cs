using System.Text;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Core.Security;
using LucidAdmin.Infrastructure;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Infrastructure.Data.Seeding;
using LucidAdmin.Web.Authentication;
using LucidAdmin.Web.Authorization;
using LucidAdmin.Web.Endpoints;
using LucidAdmin.Web.Models;
using LucidAdmin.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MudBlazor;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Praxova IT Agent API", Version = "v1" });

    // JWT Bearer authentication
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    // API Key authentication
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API Key authentication. Use X-API-Key header or Authorization: ApiKey {key}",
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "ApiKey"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Authentication - Cookie for Blazor UI, JWT for API
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "LucidAdmin";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "LucidAdmin";

builder.Services.AddAuthentication(options =>
{
    // Default scheme for Blazor is Cookie
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "LucidAdmin.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    // For API routes, return 401 instead of redirecting to login
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = 401;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = 403;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
})
.AddJwtBearer(options =>
{
    // Signing key is set after app build via PostConfigure (auto-generated,
    // stored encrypted in database, initialized before first request)
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        ClockSkew = TimeSpan.Zero
    };
})
.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", options => { })
.AddPolicyScheme("Combined", "JWT or API Key", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        // Check for API key in headers
        var apiKeyHeader = context.Request.Headers["X-API-Key"].FirstOrDefault();
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

        if (!string.IsNullOrEmpty(apiKeyHeader) ||
            (authHeader?.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return "ApiKey";
        }

        // Default to JWT Bearer for API requests
        return JwtBearerDefaults.AuthenticationScheme;
    };
});

// Authorization
builder.Services.AddAuthorization(AuthorizationPolicies.ConfigurePolicies);

// Authorization handlers
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, ServiceAccountAccessHandler>();

// HttpContextAccessor for AuthenticationStateProvider
builder.Services.AddHttpContextAccessor();

// Custom AuthenticationStateProvider for Blazor
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddCascadingAuthenticationState();

// Infrastructure services
builder.Services.AddInfrastructure(builder.Configuration);

// Web services
builder.Services.AddScoped<IServiceAccountService, LucidAdmin.Web.Services.ServiceAccountService>();
builder.Services.AddScoped<IToolServerService, LucidAdmin.Web.Services.ToolServerService>();
builder.Services.AddScoped<IAgentService, LucidAdmin.Web.Services.AgentService>();
builder.Services.AddScoped<ICapabilityMappingService, LucidAdmin.Web.Services.CapabilityMappingService>();
builder.Services.AddScoped<IAgentExportService, AgentExportService>();
builder.Services.AddSingleton<WorkflowRequirementsService>();
builder.Services.AddScoped<IAdSettingsService, AdSettingsService>();

// AD settings override file (portal-managed, persists in data volume)
var adSettingsPath = Path.Combine(
    builder.Configuration.GetValue<string>("DataDirectory") ?? "/app/data",
    "ad-settings.json");
builder.Configuration.AddJsonFile(adSettingsPath, optional: true, reloadOnChange: true);

// Active Directory options
builder.Services.Configure<ActiveDirectoryOptions>(
    builder.Configuration.GetSection(ActiveDirectoryOptions.SectionName));

// Authentication providers
builder.Services.AddScoped<LocalAuthenticationProvider>();
builder.Services.AddScoped<LdapAuthenticationProvider>();
builder.Services.AddScoped<LucidAdmin.Web.Services.IAuthenticationService, LucidAdmin.Web.Services.AuthenticationService>();

// Global JSON options — SecretString protection
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new SecretStringJsonConverter());
});

// MVC Controllers for Account management
builder.Services.AddControllers();

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// MudBlazor
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 5000;
    config.SnackbarConfiguration.HideTransitionDuration = 500;
    config.SnackbarConfiguration.ShowTransitionDuration = 500;
});

// HttpClient for Blazor components (server-side requires special handling for auth)
builder.Services.AddScoped(sp =>
{
    // For server-side Blazor, we need to create an HttpClient that can make
    // authenticated requests to our own API endpoints
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var navigationManager = sp.GetRequiredService<NavigationManager>();

    var handler = new HttpClientHandler
    {
        UseCookies = false // We'll handle cookies manually
    };

    // For loopback HTTPS calls (Blazor server calling its own API),
    // trust the server certificate. This is safe because:
    // 1. It's the server calling itself — no external trust needed
    // 2. The real TLS trust is between the client browser and the server
    // Production deployments with proper CA trust can remove this.
    var baseUri = new Uri(navigationManager.BaseUri);
    if (baseUri.IsLoopback || baseUri.Host.EndsWith(".local"))
    {
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    }

    var httpClient = new HttpClient(handler)
    {
        BaseAddress = baseUri
    };

    // Copy authentication cookie from current HTTP context
    if (httpContextAccessor.HttpContext != null)
    {
        var cookieHeader = httpContextAccessor.HttpContext.Request.Headers["Cookie"].ToString();
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            httpClient.DefaultRequestHeaders.Add("Cookie", cookieHeader);
        }
    }

    return httpClient;
});

var app = builder.Build();

// Database migration and seeding
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<LucidDbContext>();
    context.Database.Migrate();

    // Seed default admin user if not exists
    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

    var adminUser = await userRepository.GetByUsernameAsync("admin");
    if (adminUser == null)
    {
        var newAdmin = new User
        {
            Username = "admin",
            Email = "admin@praxova.local",
            PasswordHash = passwordHasher.HashPassword("admin"),
            Role = UserRole.Admin,
            IsEnabled = true,
            MustChangePassword = true
        };
        await userRepository.AddAsync(newAdmin);
        Log.Information("Default admin user created — password change required on first login");
    }
    else if (passwordHasher.VerifyPassword("admin", adminUser.PasswordHash))
    {
        // Existing DB with default password — retroactively force change
        adminUser.MustChangePassword = true;
        await userRepository.UpdateAsync(adminUser);
        Log.Information("Admin user still has default password — MustChangePassword set to true");
    }

    // Seal/Unseal: Initialize or unseal the secrets store
    var sealManager = app.Services.GetRequiredService<ISealManager>();
    var autoUnsealPassphrase = Environment.GetEnvironmentVariable("PRAXOVA_UNSEAL_PASSPHRASE");

    if (sealManager.RequiresInitialization)
    {
        if (!string.IsNullOrEmpty(autoUnsealPassphrase))
        {
            await sealManager.InitializeAsync(autoUnsealPassphrase);
            Log.Information("Secrets store initialized and unsealed via PRAXOVA_UNSEAL_PASSPHRASE");
        }
        else
        {
            Log.Warning(
                "Secrets store requires initialization. Set PRAXOVA_UNSEAL_PASSPHRASE or " +
                "use POST /api/v1/system/initialize to set the master passphrase.");
        }
    }
    else if (!string.IsNullOrEmpty(autoUnsealPassphrase))
    {
        var unsealSuccess = await sealManager.UnsealAsync(autoUnsealPassphrase);
        if (unsealSuccess)
            Log.Information("Secrets store unsealed via PRAXOVA_UNSEAL_PASSPHRASE");
        else
            Log.Error("Failed to unseal secrets store — PRAXOVA_UNSEAL_PASSPHRASE is incorrect");
    }
    else
    {
        Log.Warning("Secrets store is SEALED. Use POST /api/v1/system/unseal to provide the master passphrase.");
    }

    // Initialize JWT key manager only when unsealed (requires encryption)
    if (sealManager.IsUnsealed)
    {
        var jwtKeyManager = app.Services.GetRequiredService<IJwtKeyManager>();
        await jwtKeyManager.InitializeAsync();

        // Configure JWT Bearer signing key now that JwtKeyManager is initialized
        var jwtBearerOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>().Get(
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);
        jwtBearerOptions.TokenValidationParameters.IssuerSigningKey =
            new SymmetricSecurityKey(jwtKeyManager.GetSigningKey());
    }
    else
    {
        Log.Warning("JWT key manager skipped — secrets store is sealed. API authentication will not work until unsealed.");
    }

    // Seed built-in rulesets
    var rulesetSeeder = new LucidAdmin.Infrastructure.Data.Seeding.RulesetSeeder(
        context,
        scope.ServiceProvider.GetRequiredService<ILogger<LucidAdmin.Infrastructure.Data.Seeding.RulesetSeeder>>());
    await rulesetSeeder.SeedAsync();

    // Seed built-in ticket categories (must run before example sets)
    var ticketCategorySeeder = new LucidAdmin.Infrastructure.Data.Seeding.TicketCategorySeeder(
        context,
        scope.ServiceProvider.GetRequiredService<ILogger<LucidAdmin.Infrastructure.Data.Seeding.TicketCategorySeeder>>());
    await ticketCategorySeeder.SeedAsync();

    // Seed built-in example sets
    var exampleSetSeeder = new LucidAdmin.Infrastructure.Data.Seeding.ExampleSetSeeder(
        context,
        scope.ServiceProvider.GetRequiredService<ILogger<LucidAdmin.Infrastructure.Data.Seeding.ExampleSetSeeder>>());
    await exampleSetSeeder.SeedAsync();

    // Seed built-in workflows
    var workflowSeeder = new LucidAdmin.Infrastructure.Data.Seeding.WorkflowSeeder(
        context,
        scope.ServiceProvider.GetRequiredService<ILogger<LucidAdmin.Infrastructure.Data.Seeding.WorkflowSeeder>>());
    await workflowSeeder.SeedAsync();
}

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// TODO: TD-007 — Add API key authentication middleware
// TODO: TD-007 — Add CORS policy for known origins only

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Map MVC controllers
app.MapControllers();

// Map endpoints
app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapDashboardEndpoints();
app.MapProviderEndpoints();
app.MapCapabilityEndpoints();
app.MapServiceAccountEndpoints();
app.MapToolServerEndpoints();
app.MapAgentEndpoints();
app.MapAgentConfigurationEndpoints();
app.MapCapabilityRoutingEndpoints();
app.MapCapabilityMappingEndpoints();
app.MapAuditEventEndpoints();
app.MapCredentialEndpoints();
app.MapApiKeyEndpoints();
app.MapRulesetEndpoints();
app.MapTicketCategoryEndpoints();
app.MapExampleSetEndpoints();
app.MapWorkflowEndpoints();
app.MapManualSubmissionEndpoints();
app.MapApprovalEndpoints();
app.MapClarificationEndpoints();
app.MapSettingsEndpoints();
app.MapSystemEndpoints();

// Map Blazor
app.MapRazorComponents<LucidAdmin.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
