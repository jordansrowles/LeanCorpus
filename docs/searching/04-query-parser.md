# The query parser

`QueryParser` turns a string into a `Query`.

```csharp
var parser = new QueryParser(defaultField: "body", analyser: new StandardAnalyser());
Query q = parser.Parse("+quick brown -fox");
var hits = searcher.Search(q, 10);
```

The constructor above preserves compatibility limits for trusted input. For
user-supplied query text, pass `QueryParserOptions` so the parser bounds work
before it creates executable queries:

```csharp
var options = QueryParserOptions.Default with
{
    MaxInputChars = 16_384,
    MaxTokens = 2_048,
    MaxSyntaxDepth = 32,
    MaxQueryClauses = 1_024,
    MaxWildcardPatternChars = 256,
    MaxRegexpPatternChars = 512
};
var parser = new QueryParser("body", new StandardAnalyser(), options);

try
{
    Query query = parser.Parse(userInput);
}
catch (QueryParseException exception)
{
    // Return a bounded-query error to the caller.
}
```

`QueryParserOptions.Default` also bounds phrase token graphs by token count,
edge count, traversal steps, paths, compiled terms and generated clauses. Limit
violations throw `QueryParseException`; parser instances remain reusable after
a rejected input. The existing constructors do not apply the default options,
so applications that accept untrusted text should pass an options object.

`QueryParseException.Offset` is the zero-based UTF-16 code-unit offset in the
original query string. It includes field prefixes and phrase quotes, and errors
from analysis or complex-phrase parsing retain their position in that full
string.

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

Wildcard and range literals use the analyser's `ITermNormaliser` contract,
which must map each literal to one non-empty term. `StandardAnalyser` applies
lowercasing without stop-word removal, so `THE*` normalises to `the*`. A
literal that produces no term or multiple tokens is rejected. Custom analysers
that need analysed wildcard or range queries should implement
`ITermNormaliser`; full `IAnalyser.Analyse` remains responsible for ordinary
terms and phrases.

## Complex phrases

`ComplexPhraseQueryParser` uses the configured analyser for ordinary quoted
phrases and supports flat, single-token alternatives separated by `OR`:

```csharp
var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser());
Query query = parser.Parse("\"quick (fast OR swift) brown\"~1");
```

Each alternative group must contain at least two terms. `OR` is
case-insensitive. Every term in a phrase containing alternatives must analyse
to one linear token. Nested groups, missing or misplaced operators, and other
embedded operators are rejected. Phrases without alternative groups retain the
regular parser's graph-aware phrase analysis.

## See also

- <xref:Rowles.LeanCorpus.Search.Parsing.QueryParser>
