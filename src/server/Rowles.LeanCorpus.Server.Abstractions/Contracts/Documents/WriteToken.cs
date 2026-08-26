namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;

/// <summary>Versioned local read-your-writes token returned by an accepted bulk write.</summary>
public sealed record WriteToken(
    int Version,
    string IndexId,
    long SequenceNumber,
    long? CommitGeneration,
    long? ContentToken);
