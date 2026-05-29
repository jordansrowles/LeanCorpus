# Source-generated mapping

`LeanCorpus.SourceGen` adds typed mapping for applications that do not want to hand-build `LeanDocument` and `IndexSchema` instances. The generator emits direct C# that is suitable for Native AOT.

## Install

```bash
dotnet add package LeanCorpus
dotnet add package LeanCorpus.SourceGen
```

## Define a model

```csharp
using Rowles.LeanCorpus.Mapping;
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

The generated `ProductIndex` class contains:

| Member | Use |
|---|---|
| `Fields` | Typed field descriptors for query and sort helpers. |
| `ToDocument(Product)` | Builds a `LeanDocument` without reflection. |
| `FromStoredDocument(StoredDocument)` | Materialises a model from stored fields when every mapped member can be read back. |
| `CreateSchema()` | Builds an `IndexSchema` from the attributes. |
| `Map` | A `LeanDocumentMap<Product>` wrapper for dependency injection or generic code. |

## Index with generated code

```csharp
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

using var dir = new MMapDirectory("./index");
var config = new IndexWriterConfig
{
    Schema = ProductIndex.CreateSchema()
};

using var writer = new IndexWriter(dir, config);
writer.AddDocument(ProductIndex.ToDocument(new Product
{
    Id = "p-1",
    Title = "Source generation",
    Tags = ["mapping", "aot"],
    Price = 19.99,
    Published = DateTimeOffset.UtcNow
}));
writer.Commit();
```

`CreateSchema()` defaults to the `[LeanDocument(StrictSchema = ...)]` value. Pass `strict: false` or `strict: true` to override it for a specific writer configuration.

## Supported shapes

| Attribute | CLR type |
|---|---|
| `[LeanText]`, `[LeanString]` | `string`, `string[]`, `IReadOnlyList<string>` |
| `[LeanNumeric]` | integral types, floating-point types, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `decimal` |
| `[LeanVector]` | `float[]` with a positive `Dimension` |
| `[LeanGeoPoint]` | `LeanGeoLocation` |
| `[LeanStored]` | `string`, `byte[]` |

Temporal and decimal values need an explicit `LeanNumericEncoding`. `decimal` values that use `DecimalAsString` are stored-only and must keep `Stored = true`.

## Constraints

The generator supports non-generic, non-nested classes and structs. Mapped properties must be accessible instance properties with assignable setters or init accessors if `FromStoredDocument` can be generated. Unsupported shapes produce LCGEN diagnostics at build time.

## See also

- [Stored round-tripping](../index-management/05-stored-round-tripping.md)
- [Source generator diagnostics](../../articles/06-source-generator-diagnostics.md)
- <xref:Rowles.LeanCorpus.Mapping.LeanDocumentMap`1>
- <xref:Rowles.LeanCorpus.Mapping.Attributes.LeanDocumentAttribute>
