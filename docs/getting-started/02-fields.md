# Field types

Seven built-in field types. All implement `IField`.

| Type | Indexed | Stored | Use |
|---|---|---|---|
| `TextField` | yes (analysed) | opt-in | Body text, tokenised and analysed |
| `StringField` | yes (whole value) | opt-in | Identifiers, tags, enums — not analysed |
| `NumericField` | yes (BKD point) | opt-in | Long, double, etc. — range queries |
| `VectorField` | indexed via `.vec` | flat `.vec` file | Dense float vectors for ANN |
| `BinaryField` | doc-values backed | yes | Raw byte arrays |
| `StoredField` | values-only | yes | String, int, long, double — retrieval only |
| `GeoPointField` | yes (two numeric) | yes | Latitude/longitude — geo queries |


## StoreDocValues

Every field type accepts a `StoreDocValues` flag (default `true`). Set to `false` to skip DocValues population, cutting buffer overhead and flush I/O for fields that only need the inverted index:

```csharp
doc.Add(new TextField("body", "Full text goes here") { StoreDocValues = false });
```
Turn it off for fields you never sort, facet, collapse, or aggregate on. The inverted index still serves queries; only column-store operations are affected.

## Index a document

```csharp
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;

var doc = new LeanDocument();
doc.Add(new StringField("id", "abc-123"));
doc.Add(new TextField("body", "Full text goes here"));
doc.Add(new NumericField("price", 29.99));
doc.Add(new VectorField("embedding", new float[] { 0.1f, 0.2f, 0.3f }));
doc.Add(new BinaryField("raw", new byte[] { 0x01, 0x02, 0x03 }));
doc.Add(new StoredField("source", "import"));
doc.Add(new GeoPointField("location", 51.5074, -0.1278));
writer.AddDocument(doc);
```

## Indexed vs stored

- **Indexed**: drives query matching and scoring.
- **Stored**: available via `IndexSearcher.GetStoredFields(docId)`.

A field can be both. Vectors and stored-only fields live in `.vec` and `.fdt`, not the inverted index.

## Geo points

`GeoPointField` writes two numeric sub-fields: `name_lat` and `name_lon`. Query them directly with `GeoBoundingBoxQuery` or `GeoDistanceQuery`.

## See also

- <xref:Rowles.LeanCorpus.Document.LeanDocument>
- <xref:Rowles.LeanCorpus.Document.Fields.FieldType>
