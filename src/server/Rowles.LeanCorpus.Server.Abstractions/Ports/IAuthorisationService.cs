namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Authorises a caller for an operation.</summary>
public interface IAuthorisationService
{
    /// <summary>Checks whether the requested operation is allowed.</summary>
    ValueTask<AuthorisationDecision> AuthoriseAsync(OperationPermission permission, CancellationToken cancellationToken = default);
}
