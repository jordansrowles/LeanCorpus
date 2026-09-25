using System.Buffers;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Search.Aggregations;

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
