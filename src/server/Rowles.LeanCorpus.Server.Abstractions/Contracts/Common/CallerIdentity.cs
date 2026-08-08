namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

/// <summary>Describes the caller that initiated an operation.</summary>
/// <param name="SubjectId">Opaque host-defined subject identifier.</param>
/// <param name="AuthenticationScheme">Host-defined authentication scheme name.</param>
/// <param name="Roles">Roles or claims available to authorisation policy.</param>
/// <param name="IsAuthenticated">Whether the caller completed host authentication.</param>
public sealed record CallerIdentity(
    string SubjectId,
    string? AuthenticationScheme,
    IReadOnlyList<string> Roles,
    bool IsAuthenticated)
{
    /// <summary>Gets the anonymous Community caller.</summary>
    public static CallerIdentity Anonymous { get; } = new("anonymous", null, [], false);
}
