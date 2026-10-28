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
    MaxAnalysedTokens = 4_096,
    MaxAnalysedTokenChars = 262_144,
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

Analysis shares `MaxAnalysedTokens` and `MaxAnalysedTokenChars` across ordinary
terms and quoted phrases. The defaults are 16,384 emitted tokens and 1,048,576
UTF-16 characters. The sink checks both limits before copying token text, so an
expanding analyser cannot buffer all its output before the compiler rejects it.
Use these limits alongside `MaxQueryClauses`; graph tokens can compile into fewer
clauses than the analyser emits.

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

`ComplexPhraseQueryParser` keeps ordinary quoted phrases on the graph-aware
phrase compiler. Flat, parenthesised `OR` groups are the only supported complex
phrase slots. For example:

```csharp
var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser());
Query ordinary = parser.Parse("\"quick brown fox\"");
Query ordinaryWithSlop = parser.Parse("\"quick brown fox\"~2");
Query alternatives = parser.Parse("\"quick (fast OR swift) brown\"");
Query alternativesWithSlop = parser.Parse("\"quick (fast OR swift) brown\"~2");
```

Each alternative group must contain at least two simple, unescaped terms, and
each term must analyse to one linear token. `OR` is case-insensitive and has
grammar meaning only inside a flat parenthesised alternative group. The words
`AND`, `OR`, `NOT` and `TO` remain analyser text in an ordinary quoted phrase.
Phrases without alternatives retain token-graph-aware analysis.

`InOrder` defaults to `true` and controls whether slots in a multi-slot,
span-based complex phrase must match in query order. Setting it to `false`
exposes the existing unordered `SpanNearQuery` behaviour without changing
slop. It does not affect ordinary analysed phrases or broaden the accepted
grammar:

```csharp
var unorderedParser = new ComplexPhraseQueryParser("body", new StandardAnalyser())
{
    InOrder = false
};

Query unordered = unorderedParser.Parse("\"quick (fast OR swift) brown\"");
```

With slop `0`, `InOrder = true` matches `quick fast brown` but not
`brown fast quick`; `InOrder = false` permits either order. A single group such
as `"(fast OR swift)"` is one slot and returns `SpanOrQuery` directly, so it
has no relative ordering requirement.

Embedded query operators that cannot retain position-preserving semantics are
unsupported and throw `QueryParseException`:

```text
"foo* bar"
"foo~2 bar"
"/foo.*/ bar"
"foo^2 bar"
"foo^=2 bar"
"foo|bar baz"
"field:foo bar"
"[alpha TO omega]"
"{alpha TO omega}"
"+foo bar"
"-foo bar"
"quick (fast^2 OR swift) brown"
```

This is the LeanCorpus 4.0 parser contract: unsupported syntax is rejected
rather than simplified or translated to spans.

On the ordinary quoted-phrase path, in-token punctuation remains analyser
input:

```text
foo-bar
c++
a+b
foo/bar
```

Escaped operator characters on that path also remain analyser input:

```text
foo\^bar
foo\|bar
\+foo
\-foo
\/foo
foo\*bar
```

The configured analyser determines the resulting tokens. Escapes do not
broaden the flat alternative grammar; terms inside `(a OR b)` remain simple
and unescaped.

## See also

- <xref:Rowles.LeanCorpus.Search.Parsing.QueryParser>
