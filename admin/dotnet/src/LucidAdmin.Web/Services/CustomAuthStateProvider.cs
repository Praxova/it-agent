using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace LucidAdmin.Web.Services;

/// <summary>
/// Custom authentication state provider for Blazor Server that reads claims from HttpContext.
/// </summary>
public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CustomAuthStateProvider(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the current authentication state from the HTTP context.
    /// </summary>
    /// <returns>The authentication state containing the user's claims principal.</returns>
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            return Task.FromResult(new AuthenticationState(httpContext.User));
        }

        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        return Task.FromResult(new AuthenticationState(anonymousUser));
    }

    /// <summary>
    /// Notifies that the authentication state has changed.
    /// </summary>
    public void NotifyAuthenticationStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
