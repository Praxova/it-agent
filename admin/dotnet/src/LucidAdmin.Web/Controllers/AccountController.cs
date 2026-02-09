using System.Security.Claims;
using System.Text.RegularExpressions;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Controllers;

/// <summary>
/// Controller for account management (login/logout/change-password).
/// </summary>
[Route("account")]
public class AccountController : Controller
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditEventRepository _auditRepository;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IAuditEventRepository auditRepository,
        ILogger<AccountController> logger)
    {
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(auditRepository);
        ArgumentNullException.ThrowIfNull(logger);

        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    /// <summary>
    /// Handles user login.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromForm] string username,
        [FromForm] string password,
        [FromForm] string? returnUrl = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return Redirect($"/login?error={Uri.EscapeDataString("Username and password are required.")}&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
            }

            var user = await _userRepository.GetByUsernameAsync(username);

            if (user == null || !_passwordHasher.VerifyPassword(password, user.PasswordHash))
            {
                _logger.LogWarning("Failed login attempt for username: {Username}", username);
                return Redirect($"/login?error={Uri.EscapeDataString("Invalid username or password.")}&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
            }

            if (!user.IsEnabled)
            {
                _logger.LogWarning("Login attempt for disabled user: {Username}", username);
                return Redirect($"/login?error={Uri.EscapeDataString("Your account has been disabled.")}&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
            }

            // Check for lockout
            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
            {
                _logger.LogWarning("Login attempt for locked out user: {Username}", username);
                return Redirect($"/login?error={Uri.EscapeDataString($"Your account is locked until {user.LockoutEnd.Value:g}.")}&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
            }

            // Create claims
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("MustChangePassword", user.MustChangePassword.ToString())
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            // Update last login
            await _userRepository.UpdateLastLoginAsync(user.Id);

            // Reset failed login attempts
            await _userRepository.ResetFailedLoginAsync(user.Id);

            _logger.LogInformation("User {Username} logged in successfully", username);

            // Force password change if required
            if (user.MustChangePassword)
            {
                return Redirect("/change-password");
            }

            // Redirect to return URL or home
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return Redirect("/");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for username: {Username}", username);
            return Redirect($"/login?error={Uri.EscapeDataString("An error occurred during login. Please try again.")}&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
        }
    }

    /// <summary>
    /// Handles password change (Blazor cookie-auth flow).
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        [FromForm] string currentPassword,
        [FromForm] string newPassword,
        [FromForm] string confirmPassword)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Redirect("/change-password?error=" + Uri.EscapeDataString("Unable to identify current user."));
        }

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            return Redirect("/change-password?error=" + Uri.EscapeDataString("User not found."));
        }

        // Verify current password
        if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            return Redirect("/change-password?error=" + Uri.EscapeDataString("Current password is incorrect."));
        }

        // Verify passwords match
        if (newPassword != confirmPassword)
        {
            return Redirect("/change-password?error=" + Uri.EscapeDataString("New passwords do not match."));
        }

        // Verify new != current
        if (currentPassword == newPassword)
        {
            return Redirect("/change-password?error=" + Uri.EscapeDataString("New password must be different from current password."));
        }

        // Enforce password policy
        var policyError = ValidatePasswordPolicy(newPassword, user.Username);
        if (policyError != null)
        {
            return Redirect("/change-password?error=" + Uri.EscapeDataString(policyError));
        }

        // Update password
        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        user.MustChangePassword = false;
        await _userRepository.UpdateAsync(user);

        // Audit
        await _auditRepository.AddAsync(new AuditEvent
        {
            Action = AuditAction.PasswordChanged,
            PerformedBy = user.Username,
            TargetResource = user.Username,
            Success = true
        });

        _logger.LogInformation("User {Username} changed their password", user.Username);

        // Re-sign-in with updated claims (MustChangePassword = false)
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("MustChangePassword", "False")
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        return Redirect("/");
    }

    /// <summary>
    /// Handles user logout.
    /// </summary>
    [HttpGet("logout")]
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name ?? "Unknown";

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        _logger.LogInformation("User {Username} logged out", username);

        return Redirect("/login");
    }

    private static string? ValidatePasswordPolicy(string password, string username)
    {
        if (password.Length < 12)
            return "Password must be at least 12 characters long.";
        if (!Regex.IsMatch(password, @"[A-Z]"))
            return "Password must contain at least one uppercase letter.";
        if (!Regex.IsMatch(password, @"[a-z]"))
            return "Password must contain at least one lowercase letter.";
        if (!Regex.IsMatch(password, @"[0-9]"))
            return "Password must contain at least one digit.";
        if (!Regex.IsMatch(password, @"[!@#$%^&*()\-_+=\[\]{}|;':"",./<>?\\]"))
            return "Password must contain at least one special character.";
        if (string.Equals(password, "admin", StringComparison.OrdinalIgnoreCase))
            return "Password cannot be 'admin'.";
        if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
            return "Password cannot be the same as your username.";
        return null;
    }
}
