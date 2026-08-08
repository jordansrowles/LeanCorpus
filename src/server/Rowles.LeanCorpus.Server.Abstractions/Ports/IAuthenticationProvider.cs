namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Authenticates transport-normalised requests without depending on a web framework.</summary>
public interface IAuthenticationProvider
{
    /// <summary>Authenticates a request.</summary>
    ValueTask<AuthenticationResult> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default);
}
