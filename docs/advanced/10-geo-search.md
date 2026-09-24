# Geo search

`GeoPointField` indexes latitude/longitude pairs for bounding-box filters,
radius filters and distance sorting. New segments keep the `_lat` and `_lon`
numeric representations for compatibility and also write a two-dimensional
Packed BKD companion. Search chooses the packed or legacy representation for
each segment, so existing indexes remain searchable without reindexing.

## Index a geo point

```csharp
var doc = new LeanDocument();
doc.Add(new StringField("id", "london"));
doc.Add(new GeoPointField("location", 51.5074, -0.1278));
writer.AddDocument(doc);
```

`GeoPointField` preserves the `location_lat` and `location_lon` numeric
sub-fields, their DocValues and the stored latitude/longitude string. New
segments also write longitude and latitude into a two-dimensional Packed BKD
field. Repeated `GeoPointField` values with the same name are supported; a
document matches a filter if any of its points matches, and distance sorting
uses the nearest point in that document.

## Bounding box

```csharp
var query = new GeoBoundingBoxQuery(
    "location",
    minLat: 51.0, maxLat: 52.0,
    minLon: -1.0, maxLon: 0.0);

var hits = searcher.Search(query, topN: 20);
```

Matches documents with at least one point inside the inclusive rectangle. A
dateline-crossing rectangle is split into two longitude ranges and deduplicated.
New segments use Packed BKD cell pruning; older segments use their numeric BKD
representation.

## Distance

```csharp
var query = new GeoDistanceQuery(
    "location",
    centreLat: 51.5074, centreLon: -0.1278,
    radiusMetres: 5000);

var hits = searcher.Search(query, topN: 50);
```

Filters to documents with a point within `radiusMetres` of the centre. Packed
BKD or legacy numeric bounds prune candidates, followed by an exact Haversine
distance check. Radius zero is supported.

## Distance sorting and nearest results

Use the normal sorted search API to return the nearest points. The first page
uses an ascending Geo distance sort; later pages can use `SearchAfter` with the
last hit from the previous page.

```csharp
var query = new MatchAllDocsQuery();
var nearestFirst = SortField.GeoDistance("location", new GeoPoint(51.5074, -0.1278));
var firstPage = searcher.Search(query, topN: 20, nearestFirst);
var nextPage = searcher.SearchAfter(firstPage.ScoreDocs[^1], query, topN: 20, nearestFirst);
```

Eligible ascending single-field Geo and XY sorts use a best-first Packed BKD
Top-N traversal. Other sort directions and compound sorts use exact DocValues
sorting. Missing point fields sort after documents with points, and equal
distances use global document ID order.

## XY points

`XYPointField` indexes finite Cartesian coordinates and writes the point values
needed for exact filtering, sorting and legacy fallback execution. Repeated
fields support multiple points per document.

```csharp
var document = new LeanDocument();
document.Add(new XYPointField("position", 12.5f, -4f));
writer.AddDocument(document);

var bounds = new XYBoundingBoxQuery("position", new XYRectangle(0, -10, 20, 10));
var withinRadius = new XYDistanceQuery("position", new XYPoint(0, 0), radius: 15);
var xyNearest = SortField.XYDistance("position", new XYPoint(0, 0));
var results = searcher.Search(withinRadius, topN: 10, xyNearest);
```

XY rectangles include their edges and distance uses Euclidean coordinate
units. A zero radius is valid. `XYPointField` does not create scalar numeric
sub-fields.

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
double normalised = GeoEncodingUtils.NormaliseLongitude(181.0);
```

You do not normally need these directly. `GeoPointField` and the geo queries
call them automatically. `NormaliseLongitude` returns the equivalent longitude
in the inclusive `[-180, 180]` interval.

## Spatial foundation types

`GeoPoint`, `GeoRectangle`, `GeoCircle`, `GeoLineString`, `GeoPolygon`, and
their `XY` counterparts are immutable validated values implementing
`IGeoGeometry` or `IXYGeometry`. Polygon rings are closed and canonicalised,
Geo shell and hole relationships are checked in one unwrapped world and again
after coordinate quantisation, invalid topology is rejected, and geographic
rings crossing the International Date Line receive deterministic seam points.
Geo and XY point fields, bounding queries, distance queries and distance sorts
are public APIs. Other geometry values are available for validated application
data, but arbitrary line and polygon indexing and shape relations are not
implemented here.

## What is not supported

- Polygon, line string, or shape queries (no WKT parsing)
- Recursive prefix tree or Spatial4n strategies
- Cartesian (XY) shapes

If you need full spatial support, consider pre-filtering with bounding box or distance queries and post-processing with a spatial library.

## See also

- [Field types](../getting-started/02-fields.md)
- [Boolean queries](../searching/02-boolean-queries.md)
