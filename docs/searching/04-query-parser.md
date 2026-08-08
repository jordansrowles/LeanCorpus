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
| `[a TO z]` | Inclusive text range |
| `{a TO z}` | Exclusive text range |
| `/pattern/` | Regular expression |
| `a AND b`, `a OR b`, `a NOT b` | Explicit Boolean operators |

Empty input returns an empty `BooleanQuery` that matches nothing.

## Search overload

```csharp
var hits = searcher.Search("body", "+quick -fox", topN: 10);
```

The third arg accepts an analyser; pass `null` for the searcher default.

## Analysing multi-term queries

`AnalysingQueryParser` also analyses the literal sections of wildcard and
prefix terms:

```csharp
var parser = new AnalysingQueryParser("body", new StandardAnalyser());
Query query = parser.Parse("QUICK*");
```

## Complex phrases

`ComplexPhraseQueryParser` accepts alternatives and multi-term clauses inside
quoted phrases and lowers them to span queries:

```csharp
var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser());
Query query = parser.Parse("\"(quick OR fast) bro*\"~1");
```

Every clause in a complex phrase must target the same field.

## See also

- <xref:Rowles.LeanCorpus.Search.Parsing.QueryParser>
