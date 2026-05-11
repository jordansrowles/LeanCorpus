# Field types

LeanCorpus ships five built-in field types. All implement
<xref:Rowles.LeanCorpus.Document.Fields.IField>.

| Type | Indexed | Stored opt-in | Notes |
|---|---|---|---|
| `TextField` | yes (analysed) | yes | Tokenised body text. |
| `StringField` | yes (whole value) | yes | Identifiers, tags, enums. Not analysed. |
| `NumericField` | yes (BKD point) | yes | Long, double, etc. Range queries. |
| `VectorField` | no | flat `.vec` file | Dense float vectors for ANN. |
| `GeoPointField` | yes | yes | Stored as two derived `_lat` and `_lon` numeric fields. |

## Examples

```csharp
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;

var doc = new LeanDocument();
doc.Add(new StringField("id", "abc-123"));
doc.Add(new TextField("body", "Full text goes here"));
doc.Add(new NumericField("price", 29.99));
doc.Add(new VectorField("embedding", new float[] { 0.1f, 0.2f, 0.3f }));
doc.Add(new GeoPointField("loc", latitude: 51.5074, longitude: -0.1278));
```

## Stored vs indexed

- **Indexed**: searchable. Drives query matching and scoring.
- **Stored**: round-tripped on retrieval (`IndexSearcher.GetStoredFields(docId)`).

A field can be both. Vectors are not "indexed" in the inverted sense; they live in `.vec`.

## Geo points

`GeoPointField` writes two numeric fields under the names `name_lat` and `name_lon`.
Use those names directly with `GeoBoundingBoxQuery` or `GeoDistanceQuery`.

## See also

- <xref:Rowles.LeanCorpus.Document.LeanDocument>
- <xref:Rowles.LeanCorpus.Document.Fields.FieldType>
