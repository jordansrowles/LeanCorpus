using Rowles.LeanCorpus.Server.Abstractions.Ports;

namespace Rowles.LeanCorpus.Server.Abstractions.Community;

/// <summary>Allows operations when the host has not supplied an authorisation policy.</summary>
public sealed class CommunityAuthorisationService : IAuthorisationService
{
    /// <inheritdoc />
    public ValueTask<AuthorisationDecision> AuthoriseAsync(OperationPermission permission, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AuthorisationDecision(true));
}
