namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>A single facet bucket: a field value and how many matching documents have it.</summary>
/// <param name="Value">The field value represented by this bucket.</param>
/// <param name="Count">The number of matching documents that have this field value.</param>
public readonly record struct FacetBucket(string Value, int Count);
