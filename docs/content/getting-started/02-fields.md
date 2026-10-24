# Field types

Built-in field types implement `IField`.

| Type | Indexed | Stored | Use |
|---|---|---|---|
| `TextField` | yes (analysed) | opt-in | Body text, tokenised and analysed |
| `StringField` | yes (whole value) | opt-in | Identifiers, tags and enums: not analysed |
| `NumericField` | yes (BKD point) | opt-in | Long, double, etc.: range queries |
| `VectorField` | indexed via `.vec` | flat `.vec` file | Dense float vectors for ANN |
| `BinaryField` | doc-values backed | yes | Raw byte arrays |
| `StoredField` | values-only | yes | String, int, long and double: retrieval only |
| `GeoPointField` | yes (Packed BKD and compatibility fields) | yes | Latitude/longitude filters and distance sorts |
| `XYPointField` | yes (Packed BKD) | no | Cartesian point filters and distance sorts; point DocValues are always written |
| `LatLonShapeField` | yes (Packed BKD) | no | Geographic points, lines, rectangles, polygons and collections |
| `XYShapeField` | yes (Packed BKD) | no | Cartesian points, lines, rectangles, polygons and collections |


## StoreDocValues

DocValues support is field-specific. Shape fields do not write DocValues in
3.2. For fields that expose a `StoreDocValues` option, set it to `false` to
skip DocValues population when the field is used only for matching:

```csharp
doc.Add(new TextField("body", "Full text goes here", stored: false, boost: 1, storeDocValues: false));
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
doc.Add(new XYPointField("position", 12.5f, -4f));
doc.Add(new LatLonShapeField("park-boundary", new GeoPolygon(
    [new GeoPoint(51.50, -0.14), new GeoPoint(51.51, -0.14),
     new GeoPoint(51.51, -0.12), new GeoPoint(51.50, -0.12)])));
doc.Add(new XYShapeField("delivery-area", new XYRectangle(0, 0, 100, 75)));
writer.AddDocument(doc);
```

## Indexed vs stored

- **Indexed**: drives query matching and scoring.
- **Stored**: available via `IndexSearcher.GetStoredFields(docId)`.

A field can be both. Vectors and stored-only fields live in `.vec` and `.fdt`, not the inverted index.

## Geo points

`GeoPointField` keeps the `name_lat` and `name_lon` numeric compatibility
sub-fields and also writes a packed two-dimensional point on new segments.
Use `GeoBoundingBoxQuery`, `GeoDistanceQuery` or `SortField.GeoDistance`.

`XYPointField` writes packed Cartesian points and the point DocValues needed
for exact filters and distance sorting. Add repeated fields with the same name
to index multiple points for one document. Use `XYBoundingBoxQuery`,
`XYDistanceQuery` or `SortField.XYDistance`.

`LatLonShapeField` and `XYShapeField` index built-in points, rectangles, line
strings, polygons and geometry collections in Packed BKD. They are not stored
and do not write DocValues in 3.2. Use `GeoShapeQuery` or `XYShapeQuery` with
`SpatialRelation.Intersects`, `Within`, `Contains` or `Disjoint`. Query circles
are supported, but cannot be indexed. Shape relation rules, polygon holes,
boundary contact and multiple-value semantics are described in the [Geo search
guide](../advanced/10-geo-search.md).

## See also

- <xref:Rowles.LeanCorpus.Document.LeanDocument>
- <xref:Rowles.LeanCorpus.Document.Fields.FieldType>
