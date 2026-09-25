using System.Buffers;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Search.Aggregations;

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
