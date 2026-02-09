using LucidAdmin.Core.Enums;

namespace LucidAdmin.Core.Models;

public record AuthenticationResult
{
    public bool Success { get; init; }
    public string? Username { get; init; }
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public UserRole Role { get; init; } = UserRole.Viewer;
    public string? ErrorMessage { get; init; }
    public string AuthenticationMethod { get; init; } = "Local";
    public bool MustChangePassword { get; init; } = false;
    public Guid? UserId { get; init; }
}
