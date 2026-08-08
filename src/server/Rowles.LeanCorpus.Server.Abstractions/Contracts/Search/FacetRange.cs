namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Defines one inclusive-exclusive facet range.</summary>
public sealed record FacetRange(string Key, double? From = null, double? To = null);
