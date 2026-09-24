using System.Buffers;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Search.Aggregations;

internal interface ISpatialAggregationState
{
    string Name { get; }
    string Field { get; }
    void Collect(SegmentReader reader, int documentId);
    ISearchAggregationResult Finish();
}

internal static class SpatialAggregationValidation
{
    internal static bool IsGeoShapeField(SegmentReader reader, string field)
        => reader.Info.SpatialFields.Any(spatialField =>
            string.Equals(spatialField.FieldName, field, StringComparison.Ordinal)
            && spatialField.Kind == SpatialFieldKind.GeoShape);

    internal static void ValidateGeoField(
        string field,
        IReadOnlyList<SegmentReader> readers,
        bool allowShapes)
    {
        ArgumentNullException.ThrowIfNull(field);
        foreach (SegmentReader reader in readers)
        {
            SpatialFieldInfo? fieldInfo = reader.Info.SpatialFields.FirstOrDefault(
                spatialField => string.Equals(spatialField.FieldName, field, StringComparison.Ordinal));

            bool hasShapeDocValues = reader.TryGetShapeDocValuesFieldMetadata(
                field,
                out ShapeDocValuesFieldMetadata shapeMetadata);

            if (fieldInfo is null)
            {
                if (reader.TryGetPackedBkdFieldMetadata(field, out _) || hasShapeDocValues)
                    throw new InvalidOperationException(
                        $"Spatial field '{field}' has persisted data but no compatible spatial field metadata.");

                // Pre-3.2 Geo point fields have no SpatialFieldInfo or packed point section.
                continue;
            }

            if (fieldInfo.Kind == SpatialFieldKind.GeoPoint)
            {
                if (hasShapeDocValues)
                    throw new InvalidOperationException(
                        $"Shape DocValues field '{field}' is present on a segment identified as GeoPoint.");
                continue;
            }

            if (fieldInfo.Kind != SpatialFieldKind.GeoShape || !allowShapes)
                throw new InvalidOperationException(
                    $"Aggregation field '{field}' has spatial kind '{fieldInfo.Kind}', which is not supported by this aggregation.");

            if (!hasShapeDocValues)
                throw MissingShapeDocValues(field, reader);

            if (shapeMetadata.Kind != SpatialFieldKind.GeoShape
                || shapeMetadata.MaxDoc != reader.MaxDoc
                || !reader.TryGetPackedBkdFieldMetadata(field, out PackedBkdFieldMetadata packedMetadata)
                || shapeMetadata.RecordCount != packedMetadata.DocumentCount)
            {
                throw MissingShapeDocValues(field, reader);
            }
        }
    }

    private static InvalidOperationException MissingShapeDocValues(string field, SegmentReader reader)
        => new(
            $"Shape aggregation field '{field}' requires complete Geo Shape DocValues coverage in segment '{reader.Info.SegmentId}'. "
            + "Enable StoreDocValues for every value and reindex the affected documents.");
}

internal static class GeoPointValueAccess
{
    internal static bool TryGetPoints(
        SegmentReader reader,
        string field,
        int documentId,
        out IReadOnlyList<byte[]>? exactValues,
        out double legacyLatitude,
        out double legacyLongitude)
    {
        string exactField = GeoPointDocValues.GetFieldName(field);
        if (reader.TryGetBinaryDocValues(exactField, documentId, out IReadOnlyList<byte[]> exact))
        {
            exactValues = exact;
            legacyLatitude = default;
            legacyLongitude = default;
            return true;
        }

        bool hasLatitude = reader.TryGetNumericValue(field + "_lat", documentId, out legacyLatitude);
        bool hasLongitude = reader.TryGetNumericValue(field + "_lon", documentId, out legacyLongitude);
        if (hasLatitude != hasLongitude)
            throw new InvalidDataException($"Legacy Geo point field '{field}' has only one coordinate for document {documentId}.");

        exactValues = null;
        return hasLatitude;
    }

    internal static int GetPointCount(IReadOnlyList<byte[]>? exactValues) => exactValues?.Count ?? 1;

    internal static GeoPoint GetPoint(
        string field,
        int documentId,
        IReadOnlyList<byte[]>? exactValues,
        double legacyLatitude,
        double legacyLongitude,
        int pointIndex)
    {
        if (exactValues is null)
        {
            if (pointIndex != 0)
                throw new ArgumentOutOfRangeException(nameof(pointIndex));
            return new GeoPoint(legacyLatitude, legacyLongitude);
        }

        if ((uint)pointIndex >= (uint)exactValues.Count
            || !GeoPointDocValues.TryDecode(exactValues[pointIndex], out double latitude, out double longitude))
            throw new InvalidDataException(
                $"Geo point DocValues for field '{field}' and document {documentId} are malformed.");
        return new GeoPoint(latitude, longitude);
    }
}

internal sealed class GeoDistanceAggregationState(GeoDistanceAggregationRequest request) : ISpatialAggregationState
{
    private readonly long[] _counts = new long[request.Ranges.Count];

    public string Name => request.Name;
    public string Field => request.Field;

    public void Collect(SegmentReader reader, int documentId)
    {
        if (!GeoPointValueAccess.TryGetPoints(
                reader,
                request.Field,
                documentId,
                out IReadOnlyList<byte[]>? exactValues,
                out double legacyLatitude,
                out double legacyLongitude))
            return;

        int pointCount = GeoPointValueAccess.GetPointCount(exactValues);
        double[] rented = ArrayPool<double>.Shared.Rent(Math.Max(1, pointCount));
        try
        {
            for (int i = 0; i < pointCount; i++)
            {
                GeoPoint point = GeoPointValueAccess.GetPoint(
                    request.Field, documentId, exactValues, legacyLatitude, legacyLongitude, i);
                rented[i] = GeoEncodingUtils.HaversineDistance(
                    request.Origin.Latitude,
                    request.Origin.Longitude,
                    point.Latitude,
                    point.Longitude);
            }

            for (int rangeIndex = 0; rangeIndex < request.Ranges.Count; rangeIndex++)
            {
                GeoDistanceRange range = request.Ranges[rangeIndex];
                double lower = range.FromMetres ?? 0d;
                for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
                {
                    double distance = rented[pointIndex];
                    if (distance >= lower && (!range.ToMetres.HasValue || distance < range.ToMetres.Value))
                    {
                        _counts[rangeIndex] = checked(_counts[rangeIndex] + 1);
                        break;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(rented);
        }
    }

    public ISearchAggregationResult Finish()
    {
        var buckets = new GeoDistanceBucket[request.Ranges.Count];
        for (int i = 0; i < buckets.Length; i++)
        {
            GeoDistanceRange range = request.Ranges[i];
            buckets[i] = new GeoDistanceBucket(range.FromMetres, range.ToMetres, _counts[i]);
        }
        return new GeoDistanceAggregationResult(request.Name, request.Field, Array.AsReadOnly(buckets));
    }
}

internal sealed class GeoCentroidAggregationState(GeoCentroidAggregationRequest request) : ISpatialAggregationState
{
    private SpatialDimension? _dimension;
    private double _latitudeNumerator;
    private double _longitudeSinNumerator;
    private double _longitudeCosNumerator;
    private double _weight;
    private long _documentCount;

    public string Name => request.Name;
    public string Field => request.Field;

    public void Collect(SegmentReader reader, int documentId)
    {
        if (!SpatialAggregationValidation.IsGeoShapeField(reader, request.Field)
            && GeoPointValueAccess.TryGetPoints(
                reader,
                request.Field,
                documentId,
                out IReadOnlyList<byte[]>? exactValues,
                out double legacyLatitude,
                out double legacyLongitude))
        {
            if (_dimension is not null and not SpatialDimension.Point)
                return;

            int pointCount = GeoPointValueAccess.GetPointCount(exactValues);
            for (int i = 0; i < pointCount; i++)
            {
                GeoPoint point = GeoPointValueAccess.GetPoint(
                    request.Field, documentId, exactValues, legacyLatitude, legacyLongitude, i);
                AddGeoCoordinate(point.Latitude, point.Longitude, 1d);
            }
            _dimension = SpatialDimension.Point;
            _documentCount = checked(_documentCount + 1);
            return;
        }

        if (!reader.TryGetShapeDocValuesRecordMetadata(request.Field, documentId, out ShapeDocValuesRecordMetadata record))
            return;

        if (_dimension.HasValue && record.HighestDimension < _dimension.Value)
            return;

        if (!_dimension.HasValue || record.HighestDimension > _dimension.Value)
        {
            _dimension = record.HighestDimension;
            _latitudeNumerator = 0;
            _longitudeSinNumerator = 0;
            _longitudeCosNumerator = 0;
            _weight = 0;
            _documentCount = 0;
        }

        _latitudeNumerator += record.Accumulator0;
        _longitudeSinNumerator += record.Accumulator1;
        _longitudeCosNumerator += record.Accumulator2;
        _weight += record.Weight;
        _documentCount = checked(_documentCount + 1);
    }

    public ISearchAggregationResult Finish()
    {
        if (_dimension is null || _weight <= 0)
            return new GeoCentroidAggregationResult(request.Name, request.Field, null, null, 0);

        double latitude = _latitudeNumerator / _weight;
        double longitudeMagnitude = Math.Sqrt(
            (_longitudeSinNumerator * _longitudeSinNumerator)
            + (_longitudeCosNumerator * _longitudeCosNumerator));
        double longitude = longitudeMagnitude <= 1e-15 * _weight
            ? 0d
            : GeoEncodingUtils.NormaliseLongitude(
                Math.Atan2(_longitudeSinNumerator, _longitudeCosNumerator) * (180d / Math.PI));

        if (!double.IsFinite(latitude) || !double.IsFinite(longitude))
            throw new InvalidOperationException($"Geo centroid aggregation '{request.Name}' overflowed its finite accumulator range.");

        return new GeoCentroidAggregationResult(
            request.Name,
            request.Field,
            new GeoPoint(latitude, longitude),
            _dimension,
            _documentCount);
    }

    private void AddGeoCoordinate(double latitude, double longitude, double weight)
    {
        _latitudeNumerator += latitude * weight;
        double radians = longitude * (Math.PI / 180d);
        _longitudeSinNumerator += Math.Sin(radians) * weight;
        _longitudeCosNumerator += Math.Cos(radians) * weight;
        _weight += weight;
    }
}

internal sealed class GeoBoundsAggregationState(GeoBoundsAggregationRequest request) : ISpatialAggregationState
{
    private readonly List<LongitudeInterval>? _intervals = request.WrapLongitude ? [] : null;
    private double _minimumLatitude = double.PositiveInfinity;
    private double _maximumLatitude = double.NegativeInfinity;
    private double _minimumLongitude = double.PositiveInfinity;
    private double _maximumLongitude = double.NegativeInfinity;
    private long _documentCount;

    public string Name => request.Name;
    public string Field => request.Field;

    public void Collect(SegmentReader reader, int documentId)
    {
        if (!SpatialAggregationValidation.IsGeoShapeField(reader, request.Field)
            && GeoPointValueAccess.TryGetPoints(
                reader,
                request.Field,
                documentId,
                out IReadOnlyList<byte[]>? exactValues,
                out double legacyLatitude,
                out double legacyLongitude))
        {
            int pointCount = GeoPointValueAccess.GetPointCount(exactValues);
            for (int i = 0; i < pointCount; i++)
            {
                GeoPoint point = GeoPointValueAccess.GetPoint(
                    request.Field, documentId, exactValues, legacyLatitude, legacyLongitude, i);
                AddLatitude(point.Latitude);
                AddLongitudeInterval(point.Longitude, point.Longitude);
            }
            _documentCount = checked(_documentCount + 1);
            return;
        }

        if (!reader.TryGetShapeDocValuesRecordMetadata(request.Field, documentId, out _))
            return;

        bool contributed = false;
        reader.VisitShapeDocValuesPrimitives(request.Field, documentId, primitive =>
        {
            double minimumLatitude = Math.Min(primitive.A.Y, Math.Min(primitive.B.Y, primitive.C.Y));
            double maximumLatitude = Math.Max(primitive.A.Y, Math.Max(primitive.B.Y, primitive.C.Y));
            double minimumLongitude = Math.Min(primitive.A.X, Math.Min(primitive.B.X, primitive.C.X));
            double maximumLongitude = Math.Max(primitive.A.X, Math.Max(primitive.B.X, primitive.C.X));
            AddLatitude(minimumLatitude);
            AddLatitude(maximumLatitude);
            AddLongitudeInterval(minimumLongitude, maximumLongitude);
            contributed = true;
        });
        if (contributed)
            _documentCount = checked(_documentCount + 1);
    }

    public ISearchAggregationResult Finish()
    {
        if (_documentCount == 0)
            return new GeoBoundsAggregationResult(request.Name, request.Field, null, 0);

        double west = _minimumLongitude;
        double east = _maximumLongitude;
        if (request.WrapLongitude)
            (west, east) = GetCircularEnvelope(_intervals!);

        return new GeoBoundsAggregationResult(
            request.Name,
            request.Field,
            new GeoRectangle(_minimumLatitude, west, _maximumLatitude, east),
            _documentCount);
    }

    private void AddLatitude(double latitude)
    {
        _minimumLatitude = Math.Min(_minimumLatitude, latitude);
        _maximumLatitude = Math.Max(_maximumLatitude, latitude);
    }

    private void AddLongitudeInterval(double west, double east)
    {
        if (west > east)
            throw new InvalidDataException("A persisted Geo primitive has a wrapped longitude interval.");
        _minimumLongitude = Math.Min(_minimumLongitude, west);
        _maximumLongitude = Math.Max(_maximumLongitude, east);
        if (_intervals is not null)
        {
            if (west == 180d && east == 180d)
                _intervals.Add(new LongitudeInterval(0d, 0d));
            else
                _intervals.Add(new LongitudeInterval(west + 180d, east + 180d));
        }
    }

    private static (double West, double East) GetCircularEnvelope(List<LongitudeInterval> intervals)
    {
        intervals.Sort(static (left, right) =>
        {
            int westComparison = left.West.CompareTo(right.West);
            return westComparison != 0 ? westComparison : left.East.CompareTo(right.East);
        });

        var merged = new List<LongitudeInterval>(intervals.Count);
        foreach (LongitudeInterval interval in intervals)
        {
            if (merged.Count == 0 || interval.West > merged[^1].East)
                merged.Add(interval);
            else if (interval.East > merged[^1].East)
                merged[^1] = merged[^1] with { East = interval.East };
        }

        double largestGap = 0;
        double westCoordinate = 0;
        double eastCoordinate = 0;
        bool foundPositiveGap = false;
        for (int i = 0; i < merged.Count; i++)
        {
            LongitudeInterval current = merged[i];
            LongitudeInterval next = merged[(i + 1) % merged.Count];
            double nextWest = i + 1 == merged.Count ? next.West + 360d : next.West;
            double gap = nextWest - current.East;
            if (gap <= 0)
                continue;

            double candidateWest = NormaliseLongitude(next.West - 180d);
            double currentWest = NormaliseLongitude(westCoordinate - 180d);
            if (!foundPositiveGap || gap > largestGap || (gap == largestGap && candidateWest < currentWest))
            {
                foundPositiveGap = true;
                largestGap = gap;
                westCoordinate = next.West;
                eastCoordinate = current.East;
            }
        }

        if (!foundPositiveGap)
            return (-180d, 180d);

        double west = NormaliseLongitude(westCoordinate - 180d);
        double east = NormaliseLongitude(eastCoordinate - 180d);
        return (west, east);
    }

    private static double NormaliseLongitude(double longitude)
    {
        longitude %= 360d;
        if (longitude < -180d)
            longitude += 360d;
        if (longitude > 180d)
            longitude -= 360d;
        return longitude == 0d ? 0d : longitude;
    }

    private readonly record struct LongitudeInterval(double West, double East);
}
