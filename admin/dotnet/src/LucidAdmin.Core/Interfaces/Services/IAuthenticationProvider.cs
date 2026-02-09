using LucidAdmin.Core.Models;

namespace LucidAdmin.Core.Interfaces.Services;

public interface IAuthenticationProvider
{
    Task<AuthenticationResult> AuthenticateAsync(string username, string password);
    bool CanHandle(string username);
}
