using System.Text;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Infrastructure;
using LucidAdmin.Infrastructure.Data;
using LucidAdmin.Infrastructure.Data.Seeding;
using LucidAdmin.Web.Authentication;
using LucidAdmin.Web.Authorization;
using LucidAdmin.Web.Endpoints;
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
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Lucid Admin Portal API", Version = "v1" });

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
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"] ?? throw new InvalidOperationException("JWT secret key not configured");
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
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
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
    var httpClient = new HttpClient(new HttpClientHandler
    {
        UseCookies = false // We'll handle cookies manually
    })
    {
        BaseAddress = new Uri(navigationManager.BaseUri)
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
            Email = "admin@lucid.local",
            PasswordHash = passwordHasher.HashPassword("admin"),
            Role = UserRole.Admin,
            IsEnabled = true
        };
        await userRepository.AddAsync(newAdmin);
        Log.Information("Default admin user created (username: admin, password: admin)");
    }

    // Seed built-in rulesets
    var rulesetSeeder = new LucidAdmin.Infrastructure.Data.Seeding.RulesetSeeder(
        context,
        scope.ServiceProvider.GetRequiredService<ILogger<LucidAdmin.Infrastructure.Data.Seeding.RulesetSeeder>>());
    await rulesetSeeder.SeedAsync();

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
app.MapExampleSetEndpoints();
app.MapWorkflowEndpoints();

// Map Blazor
app.MapRazorComponents<LucidAdmin.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
