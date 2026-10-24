# Highlighting

Extract a snippet from stored text with matching terms wrapped in tags.

## Available highlighters

| Highlighter | Method | When |
|---|---|---|
| `Highlighter` | Re-analyses stored text with an analyser | General purpose; no term vectors needed |
| `PostingsHighlighter` | Uses term vector character offsets | Fast; needs term vectors with offsets |
| `TermVectorHighlighter` | Uses term vector offsets with phrase position constraints | Phrase-aware; needs term vectors with positions and offsets |
| `HybridHighlighter` | Cascading: tries `TermVectorHighlighter` first, falls back to `Highlighter` | Best of both; automatic selection |

All implement `IHighlighter`.

## Highlighter (re-analysis)

```csharp
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Highlighting;

var hl = new Highlighter(preTag: "<mark>", postTag: "</mark>", analyser: new StandardAnalyser());

Query q = new TermQuery("body", "fox");
string snippet = hl.GetBestFragment(
    text: storedBody,
    query: q,
    maxSnippetLength: 200);
```

The analyser should match the one used at index time.

## HybridHighlighter (automatic)

```csharp
var hl = new HybridHighlighter(preTag: "<em>", postTag: "</em>");

// Pass term vectors when available; falls back to re-analysis otherwise
string snippet = hl.GetBestFragment(storedBody, query, termVectors, maxSnippetLength: 250);
```

## Term vector highlighting

```csharp
var hl = new TermVectorHighlighter(preTag: "<b>", postTag: "</b>");

// Works with term vectors that have position and offset data
string snippet = hl.GetBestFragment(storedBody, query, termVectors, maxSnippetLength: 200);
```

Enable term vectors on the writer:

```csharp
var config = new IndexWriterConfig { StoreTermVectors = true };
```

## What "best fragment" means

The snippet is the highest-scoring window in the source text by query-term density, truncated to `maxSnippetLength` characters. Returns the truncated original when no terms match.

## See also

- <xref:Rowles.LeanCorpus.Search.Highlighting.Highlighter>
- <xref:Rowles.LeanCorpus.Search.Highlighting.HybridHighlighter>
- <xref:Rowles.LeanCorpus.Search.Highlighting.IHighlighter>
