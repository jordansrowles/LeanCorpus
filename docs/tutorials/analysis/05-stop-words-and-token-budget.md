# Stop words and the token budget

## Stop words

`StopWordFilter` drops tokens that match a supplied set. The library ships
`StopWords.English` and equivalents for other supported languages. `StandardAnalyser`,
`StemmedAnalyser`, `LanguageAnalyser`, and `IcuAnalyser` all use stop words as part of
their built-in pipelines.

```csharp
var filter = new StopWordFilter(StopWords.English);
```

Stop word removal happens at analysis time. Both index-time and query-time analysers
must apply the same set, otherwise removed query terms produce zero hits.

## The token budget

Per-document token count is capped via `IndexWriterConfig.MaxTokensPerDocument`
(default `0`, unlimited). When the cap is hit, behaviour is governed by
`TokenBudgetPolicy`:

- `Truncate` (default) — silently stop processing additional tokens.
- `Reject` — throw, refusing the document.

```csharp
var config = new IndexWriterConfig
{
    MaxTokensPerDocument = 100_000,
    TokenBudgetPolicy = TokenBudgetPolicy.Truncate,
};
```

A finite budget is useful when ingesting unknown user content where pathological
documents would otherwise dominate buffer memory.

## See also

- [Analysis overview](index.md)
- [Analysers](01-analysers.md)
- <xref:Rowles.LeanCorpus.Analysis.Filters.StopWordFilter>
- <xref:Rowles.LeanCorpus.Analysis.TokenBudgetPolicy>
