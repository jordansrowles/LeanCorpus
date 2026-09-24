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
data, and Geo and XY shape fields support indexing and document-level relations.

## Index shapes and query relations

`LatLonShapeField` and `XYShapeField` index built-in points, rectangles, line
strings, polygons and geometry collections. Polygon holes are supported. A
geometry collection stored in one field is one logical value. Shape fields are
indexed in Packed BKD and are not stored. They write Shape DocValues by default
for spatial metadata aggregations; pass `storeDocValues: false` to omit that
optional representation while retaining shape query support. Shape DocValues
contain quantised operational metadata, not source geometry or WKT. Circles
cannot be indexed.

```csharp
var document = new LeanDocument();
document.Add(new StringField("id", "park-17"));
document.Add(new LatLonShapeField("boundary", new GeoPolygon(
    [new GeoPoint(51.50, -0.14), new GeoPoint(51.51, -0.14),
     new GeoPoint(51.51, -0.12), new GeoPoint(51.50, -0.12)])));
document.Add(new XYShapeField("service-area", new XYRectangle(0, 0, 100, 75)));
writer.AddDocument(document);

var nearby = searcher.Search(
    new GeoShapeQuery("boundary", SpatialRelation.Intersects, new GeoCircle(51.505, -0.13, 500)),
    topN: 20);
var covered = searcher.Search(
    new XYShapeQuery("service-area", SpatialRelation.Contains, new XYPoint(50, 20)),
    topN: 20);
```

`SpatialRelation` has four document-level meanings:

| Relation | Match rule |
|---|---|
| `Intersects` | At least one indexed field value intersects the query. |
| `Within` | Every indexed field value is within the query. |
| `Contains` | One complete indexed field value contains the whole query. Separate field instances are not combined. |
| `Disjoint` | Every indexed field value is disjoint from the query. |

Edges and vertices count as touching. A document without the queried field
matches none of the relations, including `Disjoint`. A query geometry
collection is treated as one union. When a single indexed collection value has
several components, that value may collectively contain a query. Geo circles
remain analytic query shapes using the existing Haversine Earth radius;
crossing rectangles, lines and polygons handle the International Date Line.
Query circles may cross the Date Line and include polar locations.

Shape field names have a persisted Geo/XY and point/shape kind. Reusing one
field name with a conflicting spatial kind is rejected, including across
segments during merge.

## WKT and simplification

`WktReader` accepts bounded, culture-invariant two-dimensional Geo and XY WKT
for points, lines, polygons, multi-geometries and geometry collections. Geo
ordinates are longitude then latitude; XY ordinates are X then Y. Polygon
rings must be explicitly closed. Keywords are case-insensitive, and the parser
accepts ASCII whitespace, caps nested collections at depth 32 and caps a parse
at 1,000,000 coordinate positions. Z/M coordinates, empty values, circles,
SRID prefixes and unsupported WKT forms are rejected. `WktWriter` emits
deterministic uppercase text for supported geometries and represents
rectangles as equivalent polygons.

`GeoSimplifier` and `XYSimplifier` explicitly simplify lines, polygons and
geometry collections with deterministic Douglas-Peucker reduction. Geo
tolerance is in metres and uses spherical great-circle cross-track distance;
XY tolerance is in the geometry's coordinate units and uses Euclidean distance.
Zero tolerance returns the original immutable geometry. Polygon construction
rechecks closure, area, self-intersection and shell/hole relationships after
simplification and throws if the result is invalid. Point, rectangle and circle
collection components remain unchanged. Indexing never simplifies geometry
implicitly.

## What is not supported

- Recursive prefix tree or Spatial4n strategies
- Shape distance sorting
- Custom geometry implementations

If you need full spatial support, consider pre-filtering with bounding box or distance queries and post-processing with a spatial library.

## Qualification measurements

These are exploratory BenchmarkDotNet measurements from Debian 13 on .NET
11.0.100-preview.7, using an Intel Xeon E3-1220 V2 with one logical CPU
allocated. Each run used one warm-up and three measured iterations, so the
results are workload snapshots rather than regression thresholds.

| Workload | Mean | Allocated |
| --- | ---: | ---: |
| Add and flush one 10,000-vertex XY shape | 186.3 ms | 3.38 MB |
| Serialise a 10,000-primitive Shape DocValues record | 12.962 ms | 833,744 B |
| Read metadata for a 10,000-primitive record | 483.4 ns | 232 B |
| Traverse its component tree | 1.094 ms | 464 B |
| Copy two 10,000-primitive records after reading and validating them, then write the destination `.dvg` | 11.471 ms | 1,324.78 KB |
| Geo distance aggregation over 1,000 documents | 238.6 μs | 87.48 KB |
| Geo centroid aggregation over 1,000 documents | 1.423 ms | 251.53 KB |
| Geo wrapped-bounds aggregation over 1,000 documents | 4.458 ms | 1,140.84 KB |
| One-pass numeric and spatial aggregations over 1,000 documents | 6.868 ms | 1,674.07 KB |

In the measured one-record XY sample, 10,000 primitives used 337,537 bytes in
`.dvg`, 134,444 bytes in Packed BKD and 524,288 bytes of owned preparation
capacity. These sizes depend on geometry and segment contents. The relation,
WKT, simplification and per-size Shape DocValues reports are recorded in the
Sprint 4 tracker with their workloads and artefact paths.

## See also

- [Field types](../getting-started/02-fields.md)
- [Boolean queries](../searching/02-boolean-queries.md)
