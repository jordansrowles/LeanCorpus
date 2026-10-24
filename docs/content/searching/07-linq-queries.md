# LINQ queries

`LeanQueryable<T>` translates LINQ expressions into native `Query` objects. No reflection beyond `Expression.Compile()`, zero extra NuGet dependencies.

## Quick start

With the source generator:

```csharp
using Rowles.LeanCorpus.Linq;

using var searcher = new IndexSearcher(directory);
var results = ProductIndex
    .AsQueryable(searcher)
    .Where(p => p.Status == "active" && p.Price > 20.0)
    .Select(p => p.Title)
    .Take(10)
    .ToList();
```

Without the source generator, construct with a field resolver:

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

var recent = articles.Where(a => a.Year > 2023 && a.Status == "active").ToList();
```

## Supported expressions

| C# expression | Query produced |
|---|---|
| `d.Field == "value"` | `TermQuery` |
| `d.Field != "value"` | `BooleanQuery(MustNot(TermQuery))` |
| `d.Field == 42` | `RangeQuery(name, 42, 42)` |
| `d.Field > val` | `RangeQuery` exclusive lower |
| `d.Field >= val` | `RangeQuery` inclusive lower |
| `d.Field < val` | `RangeQuery` exclusive upper |
| `d.Field <= val` | `RangeQuery` inclusive upper |
| `str.Contains("sub")` | `WildcardQuery("*sub*")` |
| `str.StartsWith("pre")` | `PrefixQuery("pre")` |
| `str.EndsWith("suf")` | `WildcardQuery("*suf")` |
| `a && b` | `BooleanQuery(Must)` |
| `a \|\| b` | `BooleanQuery(Should)` |
| `!a` | `BooleanQuery(MustNot)` |

## LINQ operators

| Operator | Behaviour |
|---|---|
| `Where(predicate)` | Translates predicate to a `Query` |
| `Select(projection)` | Compiles and caches projection; applied at materialisation |
| `First()` / `FirstOrDefault()` | Executes search, returns top hit |
| `Single()` / `SingleOrDefault()` | Fetches two hits, validates count |
| `Count()` / `Count(predicate)` | Counts via `TotalHits` |
| `Any()` / `Any(predicate)` | True when results exist |
| `ToList()` / `ToArray()` | Materialises all matches |
| `Take(n)` | Limits top-N passed to searcher |
| `Skip(n)` | Fetches extra results and offsets |

## Allocation profile

The expression visitor is a stack-allocated `readonly struct`. Field lookups resolve through a `Func<string, IFieldDescriptor?>?` delegate; the source generator emits a `switch` expression the JIT compiles to a jump table. Wildcard patterns for `Contains`/`StartsWith`/`EndsWith` use `string.Concat` (single allocation of exact length). Select projections are compiled once per shape and cached in a `ConcurrentDictionary`.

## AOT

Native AOT compatible. Expression-tree resolution uses `Expression.Compile()` which the AOT compiler preserves when the selector lambda is statically visible. The source generator emits concrete field-descriptor switch expressions requiring no dynamic code.

## See also

- [Query types](01-query-types.md)
- [Source-generated mapping](../getting-started/04-source-generated-mapping.md)
- <xref:Rowles.LeanCorpus.Linq.LeanQueryable`1>
