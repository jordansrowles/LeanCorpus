namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Requests a mutable settings update.</summary>
/// <param name="IndexName">Customer-visible index name.</param>
/// <param name="Settings">Replacement mutable settings.</param>
/// <param name="ConfirmationToken">Server-issued confirmation token where policy requires one.</param>
public sealed record UpdateIndexSettingsRequest(
    string IndexName,
    MutableIndexSettings Settings,
    string? ConfirmationToken = null);
