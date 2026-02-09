using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Core.Models;
using LucidAdmin.Web.Models;
using Microsoft.Extensions.Options;

namespace LucidAdmin.Web.Services;

public interface IAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(string username, string password);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly LocalAuthenticationProvider _localProvider;
    private readonly LdapAuthenticationProvider _ldapProvider;
    private readonly IOptions<ActiveDirectoryOptions> _adOptions;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        LocalAuthenticationProvider localProvider,
        LdapAuthenticationProvider ldapProvider,
        IOptions<ActiveDirectoryOptions> adOptions,
        IUserRepository userRepository,
        ILogger<AuthenticationService> logger)
    {
        _localProvider = localProvider;
        _ldapProvider = ldapProvider;
        _adOptions = adOptions;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password)
    {
        // Rule 1: Username "admin" ALWAYS authenticates locally (break-glass)
        if (username.Equals("admin", StringComparison.OrdinalIgnoreCase))
        {
            return await _localProvider.AuthenticateAsync(username, password);
        }

        // Rule 2: If AD is enabled, try AD first
        if (_adOptions.Value.Enabled && _ldapProvider.CanHandle(username))
        {
            var adResult = await _ldapProvider.AuthenticateAsync(username, password);
            if (adResult.Success)
            {
                // Create or update shadow user record
                var shadowResult = await EnsureShadowUserAsync(adResult);
                return shadowResult;
            }

            // If AD auth failed due to connectivity, fall through to local
            // If AD auth failed due to bad credentials, return the failure
            if (adResult.ErrorMessage?.Contains("unavailable", StringComparison.OrdinalIgnoreCase) != true)
            {
                return adResult;
            }

            _logger.LogWarning("AD unavailable, falling back to local auth for {Username}", username);
        }

        // Rule 3: Try local authentication
        return await _localProvider.AuthenticateAsync(username, password);
    }

    private async Task<AuthenticationResult> EnsureShadowUserAsync(AuthenticationResult adResult)
    {
        var user = await _userRepository.GetByUsernameAsync(adResult.Username!);

        if (user == null)
        {
            // Create shadow user
            user = new User
            {
                Username = adResult.Username!,
                Email = adResult.Email ?? $"{adResult.Username}@{_adOptions.Value.Domain}",
                PasswordHash = "",
                Role = adResult.Role,
                IsEnabled = true,
                AuthenticationSource = "ActiveDirectory",
                MustChangePassword = false
            };
            await _userRepository.AddAsync(user);
            _logger.LogInformation("Created shadow user for AD account {Username} with role {Role}",
                user.Username, user.Role);
        }
        else
        {
            // Update shadow user — role may have changed in AD
            user.Role = adResult.Role;
            user.AuthenticationSource = "ActiveDirectory";
            user.Email = adResult.Email ?? user.Email;
            await _userRepository.UpdateAsync(user);
            await _userRepository.UpdateLastLoginAsync(user.Id);
        }

        return adResult with { UserId = user.Id };
    }
}
