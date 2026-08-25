using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Ports;

namespace Rowles.LeanCorpus.Server.AspNetCore.Authentication;

/// <summary>Adapts the current ASP.NET user to the transport-neutral caller identity.</summary>
public sealed class AspNetCoreAuthenticationProvider(IHttpContextAccessor httpContextAccessor) : IAuthenticationProvider
{
    /// <inheritdoc />
    public ValueTask<AuthenticationResult> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default)
    {
        HttpContext? context = httpContextAccessor.HttpContext;
        ClaimsPrincipal principal = context?.User ?? new ClaimsPrincipal();
        System.Security.Principal.IIdentity? identity = principal.Identity;
        if (identity is null || !identity.IsAuthenticated)
            return ValueTask.FromResult(new AuthenticationResult(CallerIdentity.Anonymous, false));

        string subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? principal.Identity?.Name
            ?? "authenticated";
        string? scheme = identity.AuthenticationType;
        string[] roles = principal.FindAll(ClaimTypes.Role).Select(static claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
        CallerIdentity caller = new(subject, scheme, roles, true);
        return ValueTask.FromResult(new AuthenticationResult(caller, true));
    }
}
