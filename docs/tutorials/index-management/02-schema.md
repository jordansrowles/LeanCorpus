# Schema validation

`IndexSchema` declares the expected field set. Attach to the writer and every `AddDocument` is validated.

## Define

```csharp
var schema = new IndexSchema { StrictMode = true }
    .Add(new FieldMapping("id",    FieldType.String) { IsStored = true, IsRequired = true })
    .Add(new FieldMapping("title", FieldType.Text)   { IsRequired = true })
    .Add(new FieldMapping("price", FieldType.Numeric));

var config = new IndexWriterConfig { Schema = schema };
```

## Generated schemas

With the source generator:

```csharp
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

var config = new IndexWriterConfig { Schema = ProductIndex.CreateSchema() };
```

`ProductIndex.CreateSchema()` uses the `[LeanDocument(StrictSchema = ...)]` default. Override with `ProductIndex.CreateSchema(strict: false)`.

## Strict vs lax

- `StrictMode = false` (default): unknown fields accepted silently.
- `StrictMode = true`: unknown fields throw `SchemaValidationException`.

Required fields throw regardless of mode. Type mismatches always throw.

## Per-field analyser

A `FieldMapping` can override the writer's default analyser:

```csharp
new FieldMapping("title", FieldType.Text) { Analyser = new EnglishStemmerAnalyser() }
```

## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexSchema>
- <xref:Rowles.LeanCorpus.Index.Indexer.FieldMapping>
