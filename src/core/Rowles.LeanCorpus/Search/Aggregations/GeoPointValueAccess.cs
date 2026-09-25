using System.Buffers;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Search.Aggregations;

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
