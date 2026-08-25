namespace Rowles.LeanCorpus.Server.Core.Storage;

/// <summary>Persists the local index registrations and registry format version.</summary>
public sealed record ServerRegistry(IReadOnlyList<IndexRegistration> Indices, int FormatVersion = 1);
