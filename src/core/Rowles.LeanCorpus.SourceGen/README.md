# LeanCorpus.SourceGen

[![NuGet](https://img.shields.io/nuget/v/LeanCorpus.SourceGen?label=LeanCorpus.SourceGen)](https://www.nuget.org/packages/LeanCorpus.SourceGen/)

`LeanCorpus.SourceGen` generates typed, reflection-free document maps, schemas and materialisers at build time.

## Generate your first document map

Install the core package and generator:

```bash
dotnet add package LeanCorpus
dotnet add package LeanCorpus.SourceGen
```

Define a partial model:

```csharp
using Rowles.LeanCorpus.Mapping.Attributes;

[LeanDocument]
public partial class Product
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanText("title")]
    public string? Title { get; init; }

    [LeanNumeric("price")]
    public double Price { get; init; }
}
```

Build the project. The generator emits `ProductIndex` with:

| Generated member | Use |
| --- | --- |
| `Fields` | Typed descriptors for queries and sorting |
| `ToDocument(Product)` | Converts the model into a `LeanDocument` |
| `FromStoredDocument(StoredDocument)` | Materialises a stored result |
| `CreateSchema()` | Creates an index schema from the attributes |
| `Map` | Exposes the generated `LeanDocumentMap<Product>` |

> [!IMPORTANT]
> Mapped models must be non-nested, non-generic partial classes or structs. Mapped properties need accessible instance getters and assignable setters or init accessors.

## Index with the generated map

```csharp
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

using var directory = new MMapDirectory("./products");
using var writer = new IndexWriter(directory, new IndexWriterConfig
{
    Schema = ProductIndex.CreateSchema()
});

writer.AddDocument(ProductIndex.ToDocument(new Product
{
    Id = "p-1",
    Title = "Source-generated search",
    Price = 19.99
}));
writer.Commit();
```

## Search with typed fields

```csharp
using Rowles.LeanCorpus.Search.Searcher;

using var searcher = new IndexSearcher(directory);
var results = searcher.Search(
    ProductIndex.Fields.Title.CreateTermQuery("search"),
    topN: 10);
```

You can also use `ProductIndex.AsQueryable(searcher)` for the typed LINQ provider.

Materialise a result from stored fields when the mapped shape supports round-tripping:

```csharp
using Rowles.LeanCorpus.Mapping;

foreach (var hit in results.ScoreDocs)
{
    var stored = StoredDocument.Create(searcher.GetStoredFields(hit.DocId), null);
    var product = ProductIndex.FromStoredDocument(stored);
    Console.WriteLine($"{product.Id}: {product.Title} {product.Price:C}");
}
```

## Choose an attribute

| Attribute | Use |
| --- | --- |
| `LeanText` | Analysed full text |
| `LeanString` | Exact strings and identifiers |
| `LeanNumeric` | Numeric and explicitly encoded temporal values |
| `LeanVector` | Dense vectors with a declared dimension |
| `LeanGeoPoint` | Geographic coordinates |
| `LeanStored` | Stored-only strings or binary values |
| `LeanIgnore` | Exclude a property |

Supported shapes include:

| Attribute | CLR shape |
| --- | --- |
| `LeanText`, `LeanString` | `string`, `string[]`, `IReadOnlyList<string>` |
| `LeanNumeric` | Integral and floating-point types, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `decimal` |
| `LeanVector` | `float[]` with a positive `Dimension` |
| `LeanGeoPoint` | `LeanGeoLocation` |
| `LeanStored` | `string`, `byte[]` |

Temporal and decimal values need an explicit `LeanNumericEncoding`. `DecimalAsString` is stored-only and must keep `Stored = true`.

## Diagnose generator errors

Generator diagnostics use the `LCGEN` prefix and appear during a normal build. Start with the first diagnostic, correct the model shape or attribute configuration, then rebuild.

> [!TIP]
> Inspect generated output through the IDE or compiler-generated-files support when diagnosing a model. Do not copy generated source into the project or edit files under `obj`.

## Native AOT

The generated map uses concrete code rather than runtime reflection. This makes typed mapping and field resolution suitable for trimming and Native AOT.

## Next steps

- Return to [generating your first document map](#generate-your-first-document-map).
- Use generated fields with [LINQ queries](../../../docs/searching/07-linq-queries.md).
- Learn about [stored round-tripping](../../../docs/index-management/05-stored-round-tripping.md).
