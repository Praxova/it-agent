using System.Security.Cryptography.X509Certificates;
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
    // Combined scheme routes to ApiKey, JWT, or Cookie based on request headers
    options.DefaultScheme = "Combined";
    // Challenge still goes to Cookie (redirects Blazor UI to login page)
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
        var apiKeyHeader = context.Request.Headers["X-API-Key"].FirstOrDefault();
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

        // API Key header or "ApiKey xxx" auth header
        if (!string.IsNullOrEmpty(apiKeyHeader) ||
            (authHeader?.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return "ApiKey";
        }

        // Bearer token (JWT)
        if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ?? false)
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }

        // Fall through to Cookie for Blazor UI and unauthenticated requests
        return CookieAuthenticationDefaults.AuthenticationScheme;
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
builder.Services.AddSingleton<RecoveryKeyPresenter>();
builder.Services.AddScoped<OperationTokenService>();
builder.Services.AddSingleton<OperationTokenRateLimiter>();
builder.Services.AddScoped<IAdSettingsService, AdSettingsService>();
builder.Services.AddScoped<ITlsCertificateProbeService, TlsCertificateProbeService>();
builder.Services.AddSingleton<ITrustedCertificateStore, TrustedCertificateStore>();

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
    var pkiService = sp.GetRequiredService<IInternalPkiService>();

    var handler = new HttpClientHandler
    {
        UseCookies = false
    };

    // Trust the Praxova internal CA for Blazor self-calls.
    //
    // Why this is needed: OpenSSL (used by .NET on Linux) caches the system
    // trust store at process startup. The Praxova CA is generated AFTER the
    // .NET process starts (inside InternalPkiService), so update-ca-certificates
    // runs too late for the current process. On subsequent container restarts,
    // the docker-entrypoint.sh installs the CA before .NET launches, making
    // this unnecessary — but on first boot, this is the only path that works.
    //
    // This is NOT a security bypass. It performs full X.509 chain validation
    // against the Praxova CA as a custom trust anchor.
    if (pkiService.IsInitialized)
    {
        try
        {
            var caPem = pkiService.GetCaCertificatePem();
            var caCert = X509Certificate2.CreateFromPem(caPem);

            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                // If standard OS trust validation passes, accept immediately
                if (errors == System.Net.Security.SslPolicyErrors.None)
                    return true;

                // Standard validation failed — try the Praxova CA explicitly
                if (cert == null)
                    return false;

                using var customChain = new X509Chain();
                customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                customChain.ChainPolicy.CustomTrustStore.Add(caCert);
                customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                return customChain.Build(new X509Certificate2(cert));
            };
        }
        catch (Exception ex)
        {
            // If CA loading fails, fall through to default validation.
            // This means first-boot with sealed store will use OS trust only.
            Log.Warning(ex, "Could not configure Praxova CA trust for Blazor HttpClient");
        }
    }

    var baseUri = new Uri(navigationManager.BaseUri);
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

// Background cleanup for operation token nonces (runs every minute)
var nonceCleanupTimer = new System.Threading.Timer(_ =>
{
    try
    {
        using var scope = app.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<OperationTokenService>();
        tokenService.CleanupExpiredNonces();
    }
    catch { /* Swallow — cleanup failures must not crash the portal */ }
}, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

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

    // Restore trusted certificates from data volume into OS trust store
    // This must happen early — before any TLS connections (LDAPS, tool servers, etc.)
    var trustedCertStore = app.Services.GetRequiredService<ITrustedCertificateStore>();
    await trustedCertStore.RestoreAllAsync();

    // Seal/Unseal: Initialize or unseal the secrets store
    var sealManager = app.Services.GetRequiredService<ISealManager>();
    var recoveryKeyPresenter = app.Services.GetRequiredService<RecoveryKeyPresenter>();
    var autoUnsealPassphrase = Environment.GetEnvironmentVariable("PRAXOVA_UNSEAL_PASSPHRASE");
    var autoRecoveryKey = Environment.GetEnvironmentVariable("PRAXOVA_RECOVERY_KEY");

    if (sealManager.RequiresInitialization)
    {
        if (!string.IsNullOrEmpty(autoUnsealPassphrase))
        {
            var recoveryKey = await sealManager.InitializeAsync(autoUnsealPassphrase);
            recoveryKeyPresenter.SetKey(recoveryKey);
            Log.Information("Secrets store initialized and unsealed via PRAXOVA_UNSEAL_PASSPHRASE");
            Log.Warning("╔══════════════════════════════════════════════════════════════╗");
            Log.Warning("║  RECOVERY KEY (save this — it will not be shown again):     ║");
            Log.Warning("║  {RecoveryKey}  ║", recoveryKey);
            Log.Warning("╚══════════════════════════════════════════════════════════════╝");
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
    else if (!string.IsNullOrEmpty(autoRecoveryKey))
    {
        var unsealSuccess = await sealManager.UnsealWithRecoveryKeyAsync(autoRecoveryKey);
        if (unsealSuccess)
            Log.Warning("Secrets store unsealed via PRAXOVA_RECOVERY_KEY — change the passphrase as soon as possible");
        else
            Log.Error("Failed to unseal secrets store — PRAXOVA_RECOVERY_KEY is incorrect");
    }
    else
    {
        Log.Warning("Secrets store is SEALED. Use POST /api/v1/system/unseal to provide the master passphrase.");
    }

    // Initialize crypto services only when unsealed (requires encryption)
    if (sealManager.IsUnsealed)
    {
        // --- Internal PKI: load or generate CA, ensure portal has a valid cert ---
        var pkiService = app.Services.GetRequiredService<IInternalPkiService>();

        // Try loading existing CA first
        if (!pkiService.IsInitialized)
            await pkiService.LoadAsync();

        if (!pkiService.IsInitialized)
        {
            await pkiService.InitializeAsync();
            Log.Information("Internal PKI initialized — CA generated and stored encrypted");
        }

        // Ensure admin portal has a valid TLS certificate
        var portalCertName = "admin-portal";
        var dataDir = builder.Configuration.GetValue<string>("DataDirectory") ?? "/app/data";
        var certDir = Path.Combine(dataDir, "certs");
        Directory.CreateDirectory(certDir);

        var certPath = Path.Combine(certDir, "portal-cert.pem");
        var keyPath = Path.Combine(certDir, "portal-key.pem");

        if (await pkiService.NeedsRenewalAsync(portalCertName))
        {
            // Check if this is a renewal vs first-time issuance
            var isRenewal = File.Exists(certPath);
            if (isRenewal)
            {
                var (certPem, keyPem) = await pkiService.RenewCertificateAsync(portalCertName);
                await File.WriteAllTextAsync(certPath, certPem);
                await File.WriteAllTextAsync(keyPath, keyPem);
                Log.Information("Admin portal TLS certificate renewed");
            }
            else
            {
                // First time: issue portal cert
                var (certPem, keyPem) = await pkiService.IssueCertificateAsync(
                    name: portalCertName,
                    commonName: "praxova-admin-portal",
                    sanDnsNames: new[] { "praxova-admin-portal", "admin-portal", "localhost" },
                    sanIpAddresses: new[] { "127.0.0.1", "::1" },
                    lifetimeDays: 90);
                await File.WriteAllTextAsync(certPath, certPem);
                await File.WriteAllTextAsync(keyPath, keyPem);
                Log.Information("Admin portal TLS certificate issued");
            }
        }

        // Set restrictive permissions on private key (Linux only)
        if (!OperatingSystem.IsWindows() && File.Exists(keyPath))
        {
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        // Write CA cert for trust bundle
        var caCertPath = Path.Combine(certDir, "ca.pem");
        await File.WriteAllTextAsync(caCertPath, pkiService.GetCaCertificatePem());

        // Add Praxova CA to container OS trust store so all HttpClient calls trust our certs
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var osTrustDir = "/usr/local/share/ca-certificates";
                if (Directory.Exists(osTrustDir))
                {
                    var osTrustPath = Path.Combine(osTrustDir, "praxova-internal-ca.crt");
                    File.Copy(caCertPath, osTrustPath, overwrite: true);

                    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "update-ca-certificates",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false
                    });
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        if (process.ExitCode == 0)
                            Log.Information("Praxova CA installed in OS trust store");
                        else
                            Log.Warning("update-ca-certificates exited with code {Code}", process.ExitCode);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not install Praxova CA in OS trust store");
            }
        }

        // --- JWT key manager ---
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
        Log.Warning("Crypto services skipped — secrets store is sealed. PKI and API authentication will not work until unsealed.");
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
    // HTTPS redirect for everything except the trust bundle endpoint.
    // Trust bundle must stay accessible over HTTP — it's the bootstrap mechanism
    // that enables agents to fetch the CA before they can use HTTPS.
    app.UseWhen(
        context => !(context.Request.Path.StartsWithSegments("/api/pki/trust-bundle") &&
                     context.Request.Scheme == "http"),
        branch => branch.UseHttpsRedirection());
}

// TODO: TD-007 — Add API key authentication middleware
// TODO: TD-007 — Add CORS policy for known origins only

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Force password change — redirect authenticated users with MustChangePassword=true
app.Use(async (context, next) =>
{
    var path = context.Request.Path;

    // Skip enforcement for non-authenticated users, the change-password paths, APIs, and static assets
    if (!context.User.Identity?.IsAuthenticated == true ||
        path.StartsWithSegments("/change-password") ||
        path.StartsWithSegments("/setup") ||
        path.StartsWithSegments("/account") ||
        path.StartsWithSegments("/api") ||
        path.StartsWithSegments("/_blazor") ||
        path.StartsWithSegments("/_framework") ||
        path.StartsWithSegments("/css") ||
        path.StartsWithSegments("/js") ||
        path.StartsWithSegments("/images") ||
        path.StartsWithSegments("/favicon"))
    {
        await next();
        return;
    }

    var mustChange = context.User.FindFirst("MustChangePassword")?.Value;
    if (string.Equals(mustChange, "True", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/change-password");
        return;
    }

    await next();
});

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
app.MapAuthzEndpoints();
app.MapClarificationEndpoints();
app.MapSettingsEndpoints();
app.MapSystemEndpoints();
app.MapPkiEndpoints();
app.MapTrustEndpoints();

// Map Blazor
app.MapRazorComponents<LucidAdmin.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
