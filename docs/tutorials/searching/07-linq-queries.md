# LINQ queries

LeanCorpus supports LINQ query syntax and lambda predicates through the `LeanQueryable<T>` class. Queries are translated into native `Query` objects and executed directly against the index — no reflection, no intermediate SQL, and zero additional NuGet dependencies.

## Quick start

With the source generator installed, call `AsQueryable` on the generated index class:

```csharp
using Rowles.LeanCorpus.Linq;
using Rowles.LeanCorpus.Search.Searcher;

using var searcher = new IndexSearcher(directory);
var activeDocs = ProductIndex
    .AsQueryable(searcher)
    .Where(p => p.Status == "active" && p.Price > 20.0)
    .Select(p => p.Title)
    .Take(10)
    .ToList();
```

Without the source generator, construct `LeanQueryable<T>` directly with a field resolver:

```csharp
var articles = new LeanQueryable<Article>(
    searcher, map,
    propertyName => propertyName switch
    {
        "Title"  => ArticleFields.Title,
        "Year"   => ArticleFields.Year,
        "Status" => ArticleFields.Status,
        _ => null
    });

var recent = articles
    .Where(a => a.Year > 2023 && a.Status == "active")
    .ToList();
```

## Supported operators

| C# expression | Query produced |
|---|---|
| `d.Field == "value"` | `TermQuery(name, "value")` |
| `d.Field != "value"` | `BooleanQuery(MustNot(TermQuery))` |
| `d.Field == 42` | `RangeQuery(name, 42, 42)` |
| `d.Field != 42` | `BooleanQuery(MustNot(RangeQuery 42-42))` |
| `d.Field > val` | `RangeQuery` with exclusive lower bound |
| `d.Field >= val` | `RangeQuery` with inclusive lower bound |
| `d.Field < val` | `RangeQuery` with exclusive upper bound |
| `d.Field <= val` | `RangeQuery` with inclusive upper bound |
| `str.Contains("sub")` | `WildcardQuery(name, "*sub*")` |
| `str.StartsWith("pre")` | `PrefixQuery(name, "pre")` |
| `str.EndsWith("suf")` | `WildcardQuery(name, "*suf")` |
| `a && b` | `BooleanQuery(Must(a), Must(b))` |
| `a \|\| b` | `BooleanQuery(Should(a), Should(b))` |
| `!a` | `BooleanQuery(MustNot(a))` |

## LINQ operators

| Operator | Behaviour |
|---|---|
| `Where(predicate)` | Translates the predicate to a `Query` |
| `Select(projection)` | Compiles and caches the projection; applied at materialisation |
| `First()` / `FirstOrDefault()` | Executes the search and returns the top hit |
| `Single()` / `SingleOrDefault()` | Fetches two hits and validates count |
| `Count()` / `Count(predicate)` | Counts total hits via `TotalHits` |
| `Any()` / `Any(predicate)` | Returns `true` when results exist |
| `ToList()` / `ToArray()` | Materialises all matching documents |
| `Take(n)` | Limits the top-N passed to the searcher |
| `Skip(n)` | Fetches additional results and offsets |

## Allocation profile

The expression visitor is a `readonly struct` allocated on the stack. Field lookups resolve through a
`Func<string, IFieldDescriptor?>?` delegate, the source generator emits a `switch` expression that
the JIT compiles to a jump table (zero allocation per lookup).

Wildcard patterns for `Contains` / `StartsWith` / `EndsWith` use `string.Concat` — a single
allocation of exact length. Select projections are compiled to delegates once per unique shape
and cached in a `ConcurrentDictionary`.

## AOT compatibility

The LINQ layer is Native AOT compatible. Expression-tree resolution uses no reflection beyond
`Expression.Compile()`, which the AOT compiler preserves when the selector lambda is statically
visible at the call site. The source generator emits concrete field-descriptor switch expressions
that require no dynamic code generation.

## See also

- [Query types](01-query-types.md)
- [Boolean queries](02-boolean-queries.md)
- [Source-generated mapping](../getting-started/04-source-generated-mapping.md)
- <xref:Rowles.LeanCorpus.Linq.LeanQueryable`1>
- <xref:Rowles.LeanCorpus.Linq.LeanExpressionVisitor>
