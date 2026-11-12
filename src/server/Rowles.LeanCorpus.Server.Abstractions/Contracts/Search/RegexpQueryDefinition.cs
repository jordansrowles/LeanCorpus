namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>
/// Represents a regular-expression query. The pattern must match a whole indexed term, as in
/// Lucene: <c>gov.*ment</c> matches <c>government</c>, while <c>ment</c> does not. Use explicit
/// wildcards, such as <c>.*ment.*</c>, to match within a term. Anchors such as <c>^</c> and
/// <c>$</c> are accepted and change nothing.
/// </summary>
/// <param name="Field">The indexed field whose terms the pattern is matched against.</param>
/// <param name="Pattern">A .NET regular expression that must match the whole term.</param>
public sealed record RegexpQueryDefinition(string Field, string Pattern) : QueryDefinition;
