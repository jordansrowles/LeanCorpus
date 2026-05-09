namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// Configuration for result collapsing (field grouping).
/// Keeps only the best document per unique value of the collapse field.
/// </summary>
/// <param name="FieldName">The field to collapse on; documents sharing the same value are de-duplicated.</param>
/// <param name="Mode">How to select the representative document per group.</param>
public sealed record CollapseField(string FieldName, CollapseMode Mode = CollapseMode.TopScore);
