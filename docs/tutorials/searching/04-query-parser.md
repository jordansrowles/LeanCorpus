# The query parser

`QueryParser` turns a string into a `Query`.

```csharp
var parser = new QueryParser(defaultField: "body", analyser: new StandardAnalyser());
Query q = parser.Parse("+quick brown -fox");
var hits = searcher.Search(q, 10);
```

## Grammar

| Construct | Meaning |
|---|---|
| `term` | Match default field |
| `field:term` | Match specific field |
| `"a phrase"` | Phrase query |
| `"a phrase"~2` | Phrase with slop |
| `+term` | Required clause |
| `-term` | Excluded clause |
| `(a b)` | Grouping |
| `prefix*` | Prefix query |
| `wild?card` | Wildcard query |
| `fuzzy~` | Fuzzy (default 2 edits) |
| `fuzzy~1` | Fuzzy with explicit edits |
| `term^2.5` | Boost |

Empty input returns an empty `BooleanQuery` that matches nothing.

## Search overload

```csharp
var hits = searcher.Search("body", "+quick -fox", topN: 10);
```

The third arg accepts an analyser; pass `null` for the searcher default.

## See also

- <xref:Rowles.LeanCorpus.Search.Parsing.QueryParser>
