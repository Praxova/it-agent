using System.Diagnostics;
using System.DirectoryServices.Protocols;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Exceptions;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Web.Models;
using LucidAdmin.Web.Services;
using Microsoft.AspNetCore.Mvc;
using IAuthenticationService = LucidAdmin.Web.Services.IAuthenticationService;
using Microsoft.Extensions.Options;

namespace LucidAdmin.Web.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            IAuthenticationService authService,
            ITokenService tokenService,
            IAuditEventRepository auditRepository) =>
        {
            var result = await authService.AuthenticateAsync(request.Username, request.Password);

            if (!result.Success)
            {
                await auditRepository.AddAsync(new AuditEvent
                {
                    Action = AuditAction.UserLogin,
                    PerformedBy = request.Username,
                    TargetResource = request.Username,
                    Success = false,
                    ErrorMessage = result.ErrorMessage
                });

                return Results.Unauthorized();
            }

            await auditRepository.AddAsync(new AuditEvent
            {
                Action = AuditAction.UserLogin,
                PerformedBy = result.Username!,
                TargetResource = result.Username!,
                Success = true,
                DetailsJson = $"{{\"method\":\"{result.AuthenticationMethod}\"}}"
            });

            // Build a User object for token generation
            var tokenUser = new User
            {
                Id = result.UserId ?? Guid.Empty,
                Username = result.Username!,
                Email = result.Email ?? "",
                PasswordHash = "",
                Role = result.Role,
                IsEnabled = true
            };
            var token = tokenService.GenerateToken(tokenUser);
            var expiresAt = DateTime.UtcNow.AddMinutes(60);

            return Results.Ok(new LoginResponse(
                Token: token,
                Username: result.Username!,
                Email: result.Email ?? "",
                Role: result.Role.ToString(),
                ExpiresAt: expiresAt
            ));
        });

        group.MapPost("/users", async (
            [FromBody] CreateUserRequest request,
            IUserRepository userRepository,
            IPasswordHasher passwordHasher,
            IAuditEventRepository auditRepository) =>
        {
            var existingUser = await userRepository.GetByUsernameAsync(request.Username);
            if (existingUser != null)
            {
                throw new DuplicateEntityException("User", request.Username);
            }

            var existingEmail = await userRepository.GetByEmailAsync(request.Email);
            if (existingEmail != null)
            {
                throw new ValidationException("Email", "Email already in use");
            }

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = passwordHasher.HashPassword(request.Password),
                Role = request.Role,
                IsEnabled = true
            };

            await userRepository.AddAsync(user);

            await auditRepository.AddAsync(new AuditEvent
            {
                Action = AuditAction.UserCreated,
                PerformedBy = "System",
                TargetResource = user.Username,
                Success = true
            });

            return Results.Created($"/api/auth/users/{user.Id}", new UserResponse(
                Id: user.Id,
                Username: user.Username,
                Email: user.Email,
                Role: user.Role.ToString(),
                IsEnabled: user.IsEnabled,
                LastLogin: user.LastLogin,
                CreatedAt: user.CreatedAt
            ));
        }).RequireAuthorization();

        group.MapGet("/users", async (IUserRepository userRepository) =>
        {
            var users = await userRepository.GetAllAsync();
            return Results.Ok(users.Select(u => new UserResponse(
                Id: u.Id,
                Username: u.Username,
                Email: u.Email,
                Role: u.Role.ToString(),
                IsEnabled: u.IsEnabled,
                LastLogin: u.LastLogin,
                CreatedAt: u.CreatedAt
            )));
        }).RequireAuthorization();

        group.MapGet("/ad-status", async (IOptions<ActiveDirectoryOptions> adOptions) =>
        {
            var config = adOptions.Value;

            if (!config.Enabled)
            {
                return Results.Ok(new
                {
                    enabled = false,
                    server = config.LdapServer,
                    domain = config.Domain,
                    reachable = false,
                    latencyMs = 0
                });
            }

            var sw = Stopwatch.StartNew();
            bool reachable;
            try
            {
                await Task.Run(() =>
                {
                    using var connection = new LdapConnection(
                        new LdapDirectoryIdentifier(config.LdapServer, config.LdapPort));
                    connection.AuthType = AuthType.Anonymous;
                    connection.SessionOptions.ProtocolVersion = 3;

                    if (config.UseLdaps)
                    {
                        connection.SessionOptions.SecureSocketLayer = true;
                    }

                    // Anonymous RootDSE query to test connectivity
                    var searchRequest = new SearchRequest(
                        "",
                        "(objectClass=*)",
                        SearchScope.Base,
                        "defaultNamingContext");
                    connection.SendRequest(searchRequest);
                });
                reachable = true;
            }
            catch
            {
                reachable = false;
            }
            sw.Stop();

            return Results.Ok(new
            {
                enabled = true,
                server = config.LdapServer,
                domain = config.Domain,
                reachable,
                latencyMs = sw.ElapsedMilliseconds
            });
        }).RequireAuthorization();
    }
}
