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
