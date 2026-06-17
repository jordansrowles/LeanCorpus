# Stored round-tripping

Generated maps materialise models from stored fields via `FromStoredDocument`. No separate data store lookup needed.

## Create a snapshot

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

Search APIs expose stored fields as dictionaries. Pass those into `StoredDocument.Create`, then use the generated materialiser:

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

If `tag` is absent, `Tags` is `null`. If present with values, the generated code preserves stored order.

## Numeric encodings

| CLR type | Encoding |
|---|---|
| `DateTimeOffset` | `UnixMilliseconds`, `UnixSeconds`, or `UtcTicks` |
| `DateOnly` | `DateOnlyDayNumber` |
| `TimeOnly` | `TimeOnlyTicks` |
| `decimal` | `DecimalAsString` |

Stored values are parsed with `CultureInfo.InvariantCulture`. Malformed payloads raise the underlying parse exception.

## Limitations

`FromStoredDocument` is generated only when every mapped member can be materialised from stored fields. Vector fields live in the vector store, so any mapped vector member causes the generated materialiser to throw `NotSupportedException` (emits LCGEN004 at build time).

Geo points round-trip from the stored `latitude,longitude` payload exposed by `GeoPointField`.

## See also

- [Source-generated mapping](../getting-started/04-source-generated-mapping.md)
- [Source generator diagnostics](../../articles/06-source-generator-diagnostics.md)
- <xref:Rowles.LeanCorpus.Mapping.StoredDocument>
