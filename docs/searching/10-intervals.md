# Intervals

`IntervalsQuery` builds positional conditions as a tree. It is useful when phrase and span queries are not expressive enough, such as ordered alternatives, containment, or exclusions within a bounded passage.

Intervals require indexed positions. Every source in one tree must target the same field.

## Source types

| Source | Meaning |
|---|---|
| `IntervalsTermSource` | One exact indexed term |
| `IntervalsPhraseSource` | Adjacent ordered terms |
| `IntervalsOrSource` | Any child source |
| `IntervalsOrderedSource` | Children in order with at most `MaxGaps` total gaps |
| `IntervalsUnorderedSource` | Children in any order with at most `MaxGaps` total gaps |
| `IntervalsContainingSource` | Outer intervals that contain an inner interval |
| `IntervalsContainedBySource` | Inner intervals contained by an outer interval |
| `IntervalsNotContainingSource` | Outer intervals that contain no matching inner interval |

## Ordered proximity

Match `distributed` followed by `search`, allowing up to three intervening positions:

```csharp
using Rowles.LeanCorpus.Search.Queries;

var query = new IntervalsQuery(
    new IntervalsOrderedSource(
        maxGaps: 3,
        new IntervalsTermSource("body", "distributed"),
        new IntervalsTermSource("body", "search")));

var results = searcher.Search(query, topN: 20);
```

`MaxGaps` counts gaps across the combined interval. Use `0` for adjacency.

## Alternatives

Match either `quick` or `fast`, followed by `search`:

```csharp
var speed = new IntervalsOrSource(
    new IntervalsTermSource("body", "quick"),
    new IntervalsTermSource("body", "fast"));

var query = new IntervalsQuery(
    new IntervalsOrderedSource(
        maxGaps: 2,
        speed,
        new IntervalsTermSource("body", "search")));
```

Terms are exact indexed terms. Run user input through the same analyser used for the field before constructing leaf sources.

## Containment

Match a bounded passage containing a required phrase:

```csharp
var passage = new IntervalsUnorderedSource(
    maxGaps: 12,
    new IntervalsTermSource("body", "engine"),
    new IntervalsTermSource("body", "index"));

var phrase = new IntervalsPhraseSource("body", "full", "text");

var query = new IntervalsQuery(
    new IntervalsContainingSource(passage, phrase));
```

Swap the relationship to return the inner interval:

```csharp
var inner = new IntervalsContainedBySource(phrase, passage);
```

Exclude an inner match from otherwise acceptable outer intervals:

```csharp
var query = new IntervalsQuery(
    new IntervalsNotContainingSource(
        passage,
        new IntervalsTermSource("body", "deprecated")));
```

## Choosing intervals or span queries

Use `PhraseQuery` for a fixed phrase, `MultiPhraseQuery` for alternatives at phrase positions, and span queries for direct span composition. Use intervals when containment or a deeper positional expression makes the intent clearer.

Broad alternatives and large gap limits increase positional work. Put selective terms low in the tree, avoid unbounded user-generated expansions, and measure against representative position-heavy fields.

See [Phrase and proximity](03-phrase-and-proximity.md) for the simpler query forms.
