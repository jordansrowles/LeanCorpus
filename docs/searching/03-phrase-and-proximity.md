# Phrase and proximity

Phrase, multi-phrase, span, and interval queries use indexed positions. Configure the field with positional index options before relying on these queries.

## `PhraseQuery`

`PhraseQuery` matches terms in order:

```csharp
var exact = new PhraseQuery("title", "quick", "brown", "fox");
```

Slop is the maximum number of positional gaps allowed across the phrase:

```csharp
var loose = new PhraseQuery(
    "title",
    slop: 2,
    "quick",
    "fox");
```

Slop `0` requires adjacent positions. A positive slop allows intervening positions, but `PhraseQuery` still uses the supplied term order. Use an unordered `SpanNearQuery` or `IntervalsUnorderedSource` when order must not matter.

The scorer first intersects documents containing every term, choosing a rare term as the lead, then verifies positions. Matching terms contribute their scoring factors. Slop is a match condition, not an extra proximity bonus, so a tighter occurrence does not automatically outrank a looser occurrence solely because of distance.

## `MultiPhraseQuery`

Use `MultiPhraseQuery` when one phrase position accepts alternatives:

```csharp
var query = new MultiPhraseQuery(
    field: "body",
    termGroups:
    [
        ["quick", "fast"],
        ["brown"],
        ["fox", "vixen"],
    ],
    slop: 1);
```

Explicit positions represent gaps or analysis graphs:

```csharp
var query = new MultiPhraseQuery(
    "body",
    termGroups:
    [
        ["new"],
        ["york", "nyc"],
        ["office"],
    ],
    positions: [0, 1, 3],
    slop: 0);
```

Every group must contain at least one term. Terms within a group are de-duplicated. All groups target the query field.

Multi-phrase scoring currently uses the query boost as its base score, with field boosts applied where present. Do not assume it has the same BM25 score distribution as `PhraseQuery`.

## Span leaves and alternatives

`SpanTermQuery` is the leaf:

```csharp
var machine = new SpanTermQuery("body", "machine");
var learning = new SpanTermQuery("body", "learning");
```

Combine alternatives with `SpanOrQuery`:

```csharp
var technique = new SpanOrQuery(
    new SpanTermQuery("body", "learning"),
    new SpanTermQuery("body", "training"));
```

## `SpanNearQuery`

Compose nested spans with an explicit order rule:

```csharp
var near = new SpanNearQuery(
    clauses:
    [
        machine,
        technique,
    ],
    slop: 3,
    inOrder: true);
```

Set `inOrder: false` when either order is acceptable.

## `SpanNotQuery`

Return include spans only when they do not overlap an excluded span:

```csharp
var included = new SpanNearQuery(
    [
        new SpanTermQuery("body", "search"),
        new SpanTermQuery("body", "engine"),
    ],
    slop: 4);

var excluded = new SpanTermQuery("body", "deprecated");
var query = new SpanNotQuery(included, excluded);
```

This is positional exclusion. A document can still match if it has another include span that does not overlap the excluded span.

## Performance

Positional queries read more postings data than term conjunctions. Cost grows with common terms, repeated positions, alternatives, and slop. Prefer selective terms, keep user-generated alternatives bounded, and use the simplest query that captures the requirement.

For containment and deeper positional trees, see [Intervals](10-intervals.md).
