# Stop words and the token budget

## Stop words

`StopWordFilter` drops tokens matching a supplied set. Ships `StopWords.English` and equivalents for other languages.

```csharp
var filter = new StopWordFilter(StopWords.English);
```

`StandardAnalyser`, `StemmedAnalyser`, `LanguageAnalyser`, and `IcuAnalyser` all include stop words in their built-in pipelines.

Index-time and query-time analysers must use the same set. Removed query terms produce zero hits.

## Token budget

Per-document token count is capped via `IndexWriterConfig.MaxTokensPerDocument` (default `0`, unlimited). When hit:

| Policy | Behaviour |
|---|---|
| `Truncate` (default) | Silently stop processing further tokens |
| `Reject` | Throw, refusing the document |

```csharp
var config = new IndexWriterConfig
{
    MaxTokensPerDocument = 100_000,
    TokenBudgetPolicy = TokenBudgetPolicy.Truncate,
};
```

Useful when ingesting unknown user content where pathological documents would dominate buffer memory.

## See also

- [Analysis overview](index.md)
- [Analysers](01-analysers.md)
- <xref:Rowles.LeanCorpus.Analysis.Filters.StopWordFilter>
- <xref:Rowles.LeanCorpus.Analysis.TokenBudgetPolicy>
