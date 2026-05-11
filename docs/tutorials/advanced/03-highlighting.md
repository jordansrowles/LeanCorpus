# Highlighting

`Highlighter` extracts a snippet from stored text and wraps matching terms in tags.

## Use it

```csharp
using Rowles.LeanCorpus.Search;

var hl = new Highlighter(preTag: "<mark>", postTag: "</mark>", analyser: new StandardAnalyser());

Query q = new TermQuery("body", "fox");
var terms = Highlighter.ExtractTerms(q);

string snippet = hl.GetBestFragment(
    text: storedBody,
    queryTerms: terms,
    maxSnippetLength: 200);
```

## Term extraction

`Highlighter.ExtractTerms(Query)` returns a `HashSet<string>` of lowercased terms
the query would match. It walks the query tree, including boolean and phrase
clauses.

## Tags and analyser

The default tags are `<b>` and `</b>`. The analyser should match the one used at
index time, or token boundaries will not align.

## What "best fragment" means

The snippet is the highest-scoring window in the source text by query-term density,
truncated to `maxSnippetLength` characters with ellipsis when needed. Returns the
truncated original when no terms match.

## See also

- <xref:Rowles.LeanCorpus.Search.Highlighting.Highlighter>
