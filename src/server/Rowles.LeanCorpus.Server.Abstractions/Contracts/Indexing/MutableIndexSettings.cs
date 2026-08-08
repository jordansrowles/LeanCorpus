namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Defines settings that can change without altering indexed terms.</summary>
/// <param name="RefreshInterval">Requested visibility refresh interval.</param>
/// <param name="CommitInterval">Requested commit interval.</param>
/// <param name="DefaultField">Default query field.</param>
/// <param name="MaximumQueryClauses">Maximum query clause count.</param>
public sealed record MutableIndexSettings(
    TimeSpan? RefreshInterval,
    TimeSpan? CommitInterval,
    string? DefaultField,
    int? MaximumQueryClauses);
