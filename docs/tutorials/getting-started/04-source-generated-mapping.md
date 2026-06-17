# Source-generated mapping

`LeanCorpus.SourceGen` emits typed mapping code at build time. No reflection, Native AOT compatible.

## Install

```bash
dotnet add package LeanCorpus
dotnet add package LeanCorpus.SourceGen
```

## Define a model

```csharp
using Rowles.LeanCorpus.Mapping.Attributes;

[LeanDocument]
public partial class Product
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanText("title")]
    public string? Title { get; init; }

    [LeanText("tag")]
    public IReadOnlyList<string>? Tags { get; init; }

    [LeanNumeric("price")]
    public double Price { get; init; }

    [LeanNumeric("published", Encoding = LeanNumericEncoding.UnixMilliseconds)]
    public DateTimeOffset Published { get; init; }
}
```

The generated `ProductIndex` class provides:

| Member | Use |
|---|---|
| `Fields` | Typed field descriptors for query and sort helpers |
| `ToDocument(Product)` | Builds a `LeanDocument` with no reflection |
| `FromStoredDocument(StoredDocument)` | Materialises a model from stored fields |
| `CreateSchema()` | Builds an `IndexSchema` from the attributes |
| `Map` | A `LeanDocumentMap<Product>` wrapper for DI or generic code |

## Index with generated code

```csharp
using var dir = new MMapDirectory("./index");
var config = new IndexWriterConfig { Schema = ProductIndex.CreateSchema() };
using var writer = new IndexWriter(dir, config);

writer.AddDocument(ProductIndex.ToDocument(new Product
{
    Id = "p-1", Title = "Source generation",
    Tags = ["mapping", "aot"], Price = 19.99,
    Published = DateTimeOffset.UtcNow
}));
writer.Commit();
```

## Search and materialise

```csharp
using var searcher = new IndexSearcher(dir);
var hits = searcher.Search(ProductIndex.Fields.Title.CreateTermQuery("generation"), topN: 10);

foreach (var hit in hits.ScoreDocs)
{
    var stored = StoredDocument.Create(searcher.GetStoredFields(hit.DocId), null);
    var product = ProductIndex.FromStoredDocument(stored);
    Console.WriteLine($"{product.Id}: {product.Title} £{product.Price}");
}
```

## Supported shapes

| Attribute | CLR type |
|---|---|
| `[LeanText]`, `[LeanString]` | `string`, `string[]`, `IReadOnlyList<string>` |
| `[LeanNumeric]` | integral types, floating-point, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `decimal` |
| `[LeanVector]` | `float[]` with positive `Dimension` |
| `[LeanGeoPoint]` | `LeanGeoLocation` |
| `[LeanStored]` | `string`, `byte[]` |

Temporal and decimal values need an explicit `LeanNumericEncoding`. `DecimalAsString` is stored-only; must keep `Stored = true`.

## Constraints

Non-generic, non-nested classes and structs. Mapped properties must be accessible instance properties with assignable setters or init accessors. Unsupported shapes produce LCGEN diagnostics at build time.

## See also

- [Stored round-tripping](../index-management/05-stored-round-tripping.md)
- [Source generator diagnostics](../../articles/06-source-generator-diagnostics.md)
- <xref:Rowles.LeanCorpus.Mapping.LeanDocumentMap`1>
