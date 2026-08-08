using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Ports;

namespace Rowles.LeanCorpus.Server.Abstractions.Community;

/// <summary>Returns an anonymous caller when the host has not supplied authentication.</summary>
public sealed class CommunityAuthenticationProvider : IAuthenticationProvider
{
    /// <inheritdoc />
    public ValueTask<AuthenticationResult> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AuthenticationResult(CallerIdentity.Anonymous, false));
}
