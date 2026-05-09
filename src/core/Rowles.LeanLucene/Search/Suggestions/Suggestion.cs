namespace Rowles.LeanLucene.Search.Suggestions;

/// <summary>
/// A spelling correction suggestion with its edit distance and document frequency score.
/// </summary>
public readonly record struct Suggestion(string Term, int EditDistance, int DocFrequency, float Score);
