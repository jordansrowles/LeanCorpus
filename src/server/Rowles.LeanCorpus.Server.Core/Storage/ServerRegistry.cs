namespace Rowles.LeanCorpus.Server.Core.Storage;

/// <summary>Persists the local index registrations.</summary>
public sealed record ServerRegistry(IReadOnlyList<IndexRegistration> Indices);
