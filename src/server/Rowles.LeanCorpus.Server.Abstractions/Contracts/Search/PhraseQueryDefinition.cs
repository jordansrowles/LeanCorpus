namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents an ordered phrase query.</summary>
public sealed record PhraseQueryDefinition(string Field, IReadOnlyList<string> Terms, int Slop = 0) : QueryDefinition;
