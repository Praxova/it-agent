using System.Security.Claims;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Controllers;

/// <summary>
/// Controller for account management (login/logout).
/// </summary>
[Route("account")]
public class AccountController : Controller
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ILogger<AccountController> logger)
    {
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(logger);

        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    /// <summary>
    /// Handles user login.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <param name="returnUrl">Optional return URL after successful login.</param>
    /// <returns>Redirect to return URL or home page on success, or back to login on failure.</returns>
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
                new Claim(ClaimTypes.Role, user.Role.ToString())
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
    /// Handles user logout.
    /// </summary>
    /// <returns>Redirect to login page.</returns>
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
}
