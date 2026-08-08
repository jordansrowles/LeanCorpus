namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Supplies an opaque licence envelope for private validation.</summary>
public sealed record ValidateLicenceRequest(string Envelope);
