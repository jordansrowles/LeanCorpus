# Geo search

`GeoPointField` stores latitude/longitude pairs using the existing numeric BKD-backed point representation. Geo queries use the BKD tree for fast range and distance filtering.

The 3.2 spatial foundation also provides immutable `Geo*` and `XY*` geometry
values, canonical coordinate validation, and sortable four-byte coordinate
encoding for later shape indexing. These foundation types do not add new point
query behaviour in Sprint 1.

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

`GeoEncodingUtils` encodes latitude and longitude independently into sortable
32-bit values and provides outward floor/ceil helpers for query bounds:

```csharp
int latitude = GeoEncodingUtils.EncodeLat(51.5074);
int longitude = GeoEncodingUtils.EncodeLon(-0.1278);
int lower = GeoEncodingUtils.EncodeLatFloor(51.0);
int upper = GeoEncodingUtils.EncodeLatCeil(52.0);
```

You do not normally need these directly. `GeoPointField` and the geo queries
call them automatically.

## Spatial foundation types

`GeoPoint`, `GeoRectangle`, `GeoCircle`, `GeoLineString`, `GeoPolygon`, and
their `XY` counterparts are immutable validated values. Polygon rings are
closed and canonicalised, invalid topology is rejected, and geographic rings
crossing the dateline receive deterministic seam points. Packed multidimensional
BKD persistence is internal groundwork and is not yet a public spatial query API.

## What is not supported

- Polygon, line string, or shape queries (no WKT parsing)
- Recursive prefix tree or Spatial4n strategies
- Cartesian (XY) shapes
- Multi-geo fields per document

If you need full spatial support, consider pre-filtering with bounding box or distance queries and post-processing with a spatial library.

## See also

- [Field types](../getting-started/02-fields.md)
- [Boolean queries](../searching/02-boolean-queries.md)
