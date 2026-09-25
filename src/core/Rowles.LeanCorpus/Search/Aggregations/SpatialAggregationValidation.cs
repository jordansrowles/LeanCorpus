using System.Buffers;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Search.Aggregations;

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

            bool hasPackedBkd = reader.TryGetPackedBkdFieldMetadata(field, out PackedBkdFieldMetadata packedMetadata);
            if (!hasShapeDocValues)
            {
                if (!hasPackedBkd)
                    continue;
                throw MissingShapeDocValues(field, reader);
            }

            if (!hasPackedBkd)
                throw new InvalidOperationException(
                    $"Shape DocValues field '{field}' is present without its compatible Packed BKD field in segment '{reader.Info.SegmentId}'.");

            if (shapeMetadata.Kind != SpatialFieldKind.GeoShape
                || shapeMetadata.MaxDoc != reader.MaxDoc
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
