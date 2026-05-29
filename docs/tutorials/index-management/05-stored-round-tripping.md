# Stored round-tripping

Generated maps can materialise a model from stored fields through `FromStoredDocument`. This is useful when search results need to return typed models without a separate data store lookup.

## Create a snapshot

`StoredDocument` is a small wrapper around string and binary stored-field dictionaries:

```csharp
using Rowles.LeanCorpus.Mapping;

var snapshot = StoredDocument.Create(
    fields: new Dictionary<string, IReadOnlyList<string>>
    {
        ["id"] = ["p-1"],
        ["title"] = ["Stored fields"],
        ["price"] = ["19.99"]
    },
    binaryFields: null);
```

Search APIs expose stored fields as dictionaries. Pass those dictionaries into `StoredDocument.Create`, then use the generated materialiser:

```csharp
var product = ProductIndex.FromStoredDocument(snapshot);
```

## Required fields

Required mapped members must be present in stored fields. Missing required values throw `InvalidOperationException`.

Repeated string fields preserve the distinction between a missing optional collection and an empty stored collection:

```csharp
[LeanText("tag")]
public IReadOnlyList<string>? Tags { get; init; }
```

If `tag` is absent, `Tags` is `null`. If `tag` is present with values, the generated code preserves their stored order.

## Numeric encodings

Temporal and decimal values use explicit encodings:

| CLR type | Encoding |
|---|---|
| `DateTimeOffset` | `UnixMilliseconds`, `UnixSeconds`, or `UtcTicks` |
| `DateOnly` | `DateOnlyDayNumber` |
| `TimeOnly` | `TimeOnlyTicks` |
| `decimal` | `DecimalAsString` |

Stored values are parsed with `CultureInfo.InvariantCulture`. Malformed numeric, decimal, or geo payloads raise the underlying parse exception rather than silently returning defaults.

## Limitations

`FromStoredDocument` is generated only when every mapped member can be materialised from stored fields and assigned by generated code. Vector fields live in the vector store rather than the stored-field snapshot, so any mapped vector member causes the generated materialiser to throw `NotSupportedException` and emits LCGEN004 at build time.

Geo points round-trip from the stored `latitude,longitude` payload exposed by `GeoPointField`.

## See also

- [Source-generated mapping](../getting-started/04-source-generated-mapping.md)
- [Source generator diagnostics](../../articles/06-source-generator-diagnostics.md)
- <xref:Rowles.LeanCorpus.Mapping.StoredDocument>
