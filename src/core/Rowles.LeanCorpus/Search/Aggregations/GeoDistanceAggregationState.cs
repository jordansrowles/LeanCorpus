using System.Buffers;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Search.Aggregations;

internal sealed class GeoDistanceAggregationState(GeoDistanceAggregationRequest request) : ISpatialAggregationState
{
    private readonly long[] _counts = new long[request.Ranges.Count];

    public string Name => request.Name;
    public string Field => request.Field;

    public void Collect(SpatialDocumentValues document)
    {
        if (!document.TryGetPoints(request.Field, out ReadOnlySpan<GeoPoint> points))
            return;

        int pointCount = points.Length;
        double[] rented = ArrayPool<double>.Shared.Rent(Math.Max(1, pointCount));
        try
        {
            for (int i = 0; i < pointCount; i++)
            {
                GeoPoint point = points[i];
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
