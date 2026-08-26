using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;

namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Defines a paged search request.</summary>
public sealed record SearchRequest(
    QueryDefinition Query,
    int Size = 10,
    IReadOnlyList<object?>? SearchAfter = null,
    IReadOnlyList<SortDefinition>? Sort = null,
    IReadOnlyList<FacetDefinition>? Facets = null,
    RequestedConsistency Consistency = RequestedConsistency.Local,
    bool IncludeDocuments = true,
    bool IncludeHighlights = false,
    WriteToken? ReadToken = null);
