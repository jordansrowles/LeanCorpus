using System.Text.Json;

namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents one search result without binding to the storage document type.</summary>
public sealed record SearchHit(string DocumentId, float Score, JsonElement? Document, IReadOnlyDictionary<string, IReadOnlyList<string>>? Highlights = null, IReadOnlyList<object?>? SortValues = null);
