using System.Security.Claims;
using System.Text.RegularExpressions;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Interfaces.Services;
using LucidAdmin.Web.Services;
using Microsoft.AspNetCore.Authentication;
using IAuthenticationService = LucidAdmin.Web.Services.IAuthenticationService;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidAdmin.Web.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private readonly IAuthenticationService _authService;
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditEventRepository _auditRepository;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IAuthenticationService authService,
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IAuditEventRepository auditRepository,
        ILogger<AccountController> logger)
    {
        _authService = authService;
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _auditRepository = auditRepository;
        _logger = logger;
    }

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

            var result = await _authService.AuthenticateAsync(username, password);

            if (!result.Success)
            {
                await _auditRepository.AddAsync(new AuditEvent
                {
                    Action = AuditAction.UserLogin,
                    PerformedBy = username,
                    TargetResource = username,
                    Success = false,
                    ErrorMessage = result.ErrorMessage
                });

                return Redirect($"/login?error={Uri.EscapeDataString(result.ErrorMessage ?? "Authentication failed")}&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
            }

            // Build claims from AuthenticationResult
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, result.Username!),
                new Claim(ClaimTypes.Email, result.Email ?? ""),
                new Claim(ClaimTypes.Role, result.Role.ToString()),
                new Claim("MustChangePassword", result.MustChangePassword.ToString()),
                new Claim("AuthenticationMethod", result.AuthenticationMethod)
            };

            if (result.UserId.HasValue)
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, result.UserId.Value.ToString()));
            }

            if (!string.IsNullOrEmpty(result.DisplayName))
            {
                claims.Add(new Claim("DisplayName", result.DisplayName));
            }

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

            await _auditRepository.AddAsync(new AuditEvent
            {
                Action = AuditAction.UserLogin,
                PerformedBy = result.Username!,
                TargetResource = result.Username!,
                Success = true,
                DetailsJson = $"{{\"method\":\"{result.AuthenticationMethod}\"}}"
            });

            _logger.LogInformation("User {Username} logged in via {Method}", result.Username, result.AuthenticationMethod);

            // Force password change if required (local accounts only)
            if (result.MustChangePassword)
            {
                return Redirect("/change-password");
            }

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

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        [FromForm] string currentPassword,
        [FromForm] string newPassword,
        [FromForm] string confirmPassword)
    {
        // AD users cannot change password through the portal
        var authMethod = User.FindFirstValue("AuthenticationMethod");
        if (string.Equals(authMethod, "ActiveDirectory", StringComparison.OrdinalIgnoreCase))
        {
            return Redirect("/change-password?error=" + Uri.EscapeDataString("Active Directory users must change their password through AD."));
        }

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

        if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            return Redirect("/change-password?error=" + Uri.EscapeDataString("Current password is incorrect."));
        }

        if (newPassword != confirmPassword)
        {
            return Redirect("/change-password?error=" + Uri.EscapeDataString("New passwords do not match."));
        }

        if (currentPassword == newPassword)
        {
            return Redirect("/change-password?error=" + Uri.EscapeDataString("New password must be different from current password."));
        }

        var policyError = ValidatePasswordPolicy(newPassword, user.Username);
        if (policyError != null)
        {
            return Redirect("/change-password?error=" + Uri.EscapeDataString(policyError));
        }

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        user.MustChangePassword = false;
        await _userRepository.UpdateAsync(user);

        await _auditRepository.AddAsync(new AuditEvent
        {
            Action = AuditAction.PasswordChanged,
            PerformedBy = user.Username,
            TargetResource = user.Username,
            Success = true
        });

        _logger.LogInformation("User {Username} changed their password", user.Username);

        // Re-sign-in with updated claims
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("MustChangePassword", "False"),
            new Claim("AuthenticationMethod", "Local")
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

    private static readonly Lazy<HashSet<string>> _commonPasswords = new(() =>
    {
        var assembly = typeof(AccountController).Assembly;
        using var stream = assembly.GetManifestResourceStream("LucidAdmin.Web.Resources.CommonPasswords.txt");
        if (stream == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var reader = new System.IO.StreamReader(stream);
        var passwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                passwords.Add(trimmed);
        }
        return passwords;
    });

    internal static string? ValidatePasswordPolicy(string password, string username)
    {
        if (password.Length > 128)
            return "Password must be no longer than 128 characters.";
        if (password.Length < 12)
            return "Password must be at least 12 characters long.";

        int complexityScore = 0;
        if (Regex.IsMatch(password, @"[A-Z]")) complexityScore++;
        if (Regex.IsMatch(password, @"[a-z]")) complexityScore++;
        if (Regex.IsMatch(password, @"[0-9]")) complexityScore++;
        if (Regex.IsMatch(password, @"[!@#$%^&*()\-_+=\[\]{}|;':"",./<>?\\]")) complexityScore++;

        if (complexityScore < 3)
            return "Password must contain at least 3 of: uppercase letter, lowercase letter, number, special character.";

        if (password.Contains("admin", StringComparison.OrdinalIgnoreCase))
            return "Password cannot contain the word 'admin'.";
        if (username.Length >= 3 && password.Contains(username, StringComparison.OrdinalIgnoreCase))
            return "Password cannot contain your username.";

        if (_commonPasswords.Value.Contains(password))
            return "This password is too commonly used — please choose a different password.";

        return null;
    }
}
