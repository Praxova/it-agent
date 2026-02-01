using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Exceptions;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            IUserRepository userRepository,
            IPasswordHasher passwordHasher,
            ITokenService tokenService,
            IAuditEventRepository auditRepository) =>
        {
            var user = await userRepository.GetByUsernameAsync(request.Username);
            if (user == null || !passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            {
                await auditRepository.AddAsync(new AuditEvent
                {
                    Action = AuditAction.UserLogin,
                    PerformedBy = request.Username,
                    TargetResource = request.Username,
                    Success = false,
                    ErrorMessage = "Invalid username or password"
                });

                return Results.Unauthorized();
            }

            if (!user.IsEnabled)
            {
                return Results.Problem("Account is disabled", statusCode: 403);
            }

            await userRepository.UpdateLastLoginAsync(user.Id);
            await userRepository.ResetFailedLoginAsync(user.Id);

            var token = tokenService.GenerateToken(user);
            var expiresAt = DateTime.UtcNow.AddMinutes(60);

            await auditRepository.AddAsync(new AuditEvent
            {
                Action = AuditAction.UserLogin,
                PerformedBy = user.Username,
                TargetResource = user.Username,
                Success = true
            });

            return Results.Ok(new LoginResponse(
                Token: token,
                Username: user.Username,
                Email: user.Email,
                Role: user.Role.ToString(),
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
    }
}
