using System.Text.Json.Serialization;

namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Base type for a search query without exposing engine query types.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(QueryStringDefinition), "queryString")]
[JsonDerivedType(typeof(TermQueryDefinition), "term")]
[JsonDerivedType(typeof(BooleanQueryDefinition), "boolean")]
[JsonDerivedType(typeof(PhraseQueryDefinition), "phrase")]
[JsonDerivedType(typeof(PrefixQueryDefinition), "prefix")]
[JsonDerivedType(typeof(WildcardQueryDefinition), "wildcard")]
[JsonDerivedType(typeof(RegexpQueryDefinition), "regexp")]
[JsonDerivedType(typeof(SpanNearQueryDefinition), "spanNear")]
[JsonDerivedType(typeof(VectorQueryDefinition), "vector")]
public closed record class QueryDefinition;
