# Geo search

`GeoPointField` stores latitude/longitude pairs as a 64-bit encoded value. Geo queries use the BKD tree for fast range and distance filtering.

## Index a geo point

```csharp
var doc = new LeanDocument();
doc.Add(new StringField("id", "london"));
doc.Add(new GeoPointField("location", 51.5074, -0.1278));
writer.AddDocument(doc);
```

`GeoPointField` writes two numeric sub-fields internally: `location_lat` and `location_lon`. It populates `NumericDocValues` for sorting and emits a BKD point for spatial queries.

## Bounding box

```csharp
var query = new GeoBoundingBoxQuery(
    "location",
    minLat: 51.0, maxLat: 52.0,
    minLon: -1.0, maxLon: 0.0);

var hits = searcher.Search(query, topN: 20);
```

Matches documents whose geo point falls inside the rectangle. The query is backed by a BKD range intersection — no full-table scan.

## Distance

```csharp
var query = new GeoDistanceQuery(
    "location",
    centreLat: 51.5074, centreLon: -0.1278,
    radiusMetres: 5000);

var hits = searcher.Search(query, topN: 50);
```

Filters to documents within `radiusMetres` of the centre point. Uses a BKD bounding-box approximation followed by an exact Haversine distance check on the shortlist.

## Combining with text

Geo queries compose with text queries via `BooleanQuery`:

```csharp
var bq = new BooleanQuery.Builder()
    .Add(new TermQuery("category", "restaurant"), Occur.Must)
    .Add(new GeoDistanceQuery("location", 51.5, -0.12, 1000), Occur.Filter)
    .Build();
```

Use `Occur.Filter` for geo clauses that should restrict results without affecting the BM25 score.

## Encoding

`GeoEncodingUtils` encodes and decodes lat/lon pairs into the 64-bit Morton-like interleaved representation used internally:

```csharp
long encoded = GeoEncodingUtils.Encode(51.5074, -0.1278);
(double lat, double lon) = GeoEncodingUtils.Decode(encoded);
```

You don't normally need these directly — `GeoPointField` and the geo queries call them automatically.

## What is not supported

- Polygon, line string, or shape queries (no WKT parsing)
- Recursive prefix tree or Spatial4n strategies
- Cartesian (XY) shapes
- Multi-geo fields per document

If you need full spatial support, consider pre-filtering with bounding box or distance queries and post-processing with a spatial library.

## See also

- [Field types](../getting-started/02-fields.md)
- [Boolean queries](../searching/02-boolean-queries.md)
