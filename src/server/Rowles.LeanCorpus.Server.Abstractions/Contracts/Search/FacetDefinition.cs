namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Requests a terms or range facet.</summary>
public sealed record FacetDefinition(string Name, string Field, FacetKind Kind, int? Size = null, IReadOnlyList<FacetRange>? Ranges = null);
