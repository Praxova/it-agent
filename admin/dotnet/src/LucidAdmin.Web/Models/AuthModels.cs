using System.Text.Json.Serialization;
using LucidAdmin.Core.Enums;

namespace LucidAdmin.Web.Models;

public record LoginRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password
);

public record LoginResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("expires_at")] DateTime ExpiresAt
);

public record CreateUserRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("role")] UserRole Role
);

public record UpdateUserRequest(
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("role")] UserRole? Role,
    [property: JsonPropertyName("is_enabled")] bool? IsEnabled
);

public record ChangePasswordRequest(
    [property: JsonPropertyName("current_password")] string CurrentPassword,
    [property: JsonPropertyName("new_password")] string NewPassword
);

public record UserResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("is_enabled")] bool IsEnabled,
    [property: JsonPropertyName("authentication_source")] string AuthenticationSource,
    [property: JsonPropertyName("last_login")] DateTime? LastLogin,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt
);
