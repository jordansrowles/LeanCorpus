# The query parser

<xref:Rowles.LeanCorpus.Search.Parsing.QueryParser> turns a string into a `Query`.

```csharp
var parser = new QueryParser(defaultField: "body", analyser: new StandardAnalyser());
Query q = parser.Parse("+quick brown -fox");
var hits = searcher.Search(q, 10);
```

## Grammar

| Construct | Meaning |
|---|---|
| `term` | matches the default field |
| `field:term` | matches a specific field |
| `"a phrase"` | phrase query |
| `"a phrase"~2` | phrase with slop |
| `+term` | required clause |
| `-term` | excluded clause |
| `(a b)` | grouping |
| `prefix*` | prefix query |
| `wild?card` | wildcard query |
| `fuzzy~` | fuzzy query (default 2 edits) |
| `fuzzy~1` | fuzzy with explicit edits |
| `term^2.5` | boost |

Empty input returns an empty `BooleanQuery` that matches nothing.

## Search overload

Pass a string straight in:

```csharp
var hits = searcher.Search("body", "+quick -fox", topN: 10);
```

The third arg overload accepts an analyser; pass `null` to use the searcher default.

## See also

- <xref:Rowles.LeanCorpus.Search.Parsing.QueryParser>
