using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Core.Models;

namespace LucidAdmin.Web.Services;

public class LocalAuthenticationProvider : IAuthenticationProvider
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<LocalAuthenticationProvider> _logger;

    public LocalAuthenticationProvider(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ILogger<LocalAuthenticationProvider> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public bool CanHandle(string username) => true;

    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password)
    {
        var user = await _userRepository.GetByUsernameAsync(username);

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Invalid username or password"
            };
        }

        if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            // Increment failed login count
            await _userRepository.IncrementFailedLoginAsync(user.Id);
            _logger.LogWarning("Failed local login attempt for {Username}", username);

            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Invalid username or password"
            };
        }

        if (!user.IsEnabled)
        {
            _logger.LogWarning("Login attempt for disabled local user: {Username}", username);
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = "Your account has been disabled."
            };
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
        {
            _logger.LogWarning("Login attempt for locked out user: {Username}", username);
            return new AuthenticationResult
            {
                Success = false,
                ErrorMessage = $"Your account is locked until {user.LockoutEnd.Value:g}."
            };
        }

        // Success — update tracking
        await _userRepository.UpdateLastLoginAsync(user.Id);
        await _userRepository.ResetFailedLoginAsync(user.Id);

        return new AuthenticationResult
        {
            Success = true,
            UserId = user.Id,
            Username = user.Username,
            DisplayName = user.Username,
            Email = user.Email,
            Role = user.Role,
            AuthenticationMethod = "Local",
            MustChangePassword = user.MustChangePassword
        };
    }
}
