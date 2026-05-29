# Schema validation

`IndexSchema` declares the expected field set and type per field. When attached to
the writer, every `AddDocument` call is validated.

## Define a schema

```csharp
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Document.Fields;

var schema = new IndexSchema { StrictMode = true }
    .Add(new FieldMapping("id",    FieldType.String) { IsStored = true, IsRequired = true })
    .Add(new FieldMapping("title", FieldType.Text)   { IsRequired = true })
    .Add(new FieldMapping("price", FieldType.Numeric));

var config = new IndexWriterConfig { Schema = schema };
```

## Generated schemas

If the source generator package is installed, annotated models can produce the same schema without restating field names:

```csharp
using Rowles.LeanCorpus.Mapping.Attributes;

[LeanDocument]
public partial class Product
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanText("title", Required = true)]
    public required string Title { get; init; }

    [LeanNumeric("price")]
    public double Price { get; init; }
}

var config = new IndexWriterConfig
{
    Schema = ProductIndex.CreateSchema()
};
```

`ProductIndex.CreateSchema()` uses the `[LeanDocument(StrictSchema = ...)]` default, and `ProductIndex.CreateSchema(strict: false)` can override it at the call site.

## Strict vs lax mode

- `StrictMode = false` (default): unknown fields are accepted silently.
- `StrictMode = true`: unknown fields throw `SchemaValidationException`.

Required fields, when missing, throw regardless of mode. Type mismatches always
throw.

## Per-field analyser

A `FieldMapping` can override the writer's default analyser for that field by
setting `Analyser`.

## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexSchema>
- <xref:Rowles.LeanCorpus.Index.Indexer.FieldMapping>
- <xref:Rowles.LeanCorpus.Mapping.LeanDocumentMap`1>
