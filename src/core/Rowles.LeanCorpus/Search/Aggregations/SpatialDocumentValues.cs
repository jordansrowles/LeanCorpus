using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>
/// The current document's spatial values, decoded once and shared by every spatial aggregation
/// state of one traversal. Segment-level facts (whether a field is a shape field, its exact point
/// column and legacy field names) are resolved once per segment; a document's shape record and
/// points are decoded once per document, whichever state asks first.
/// </summary>
/// <remarks>
/// One instance serves one collector on one thread. A cached point column is used only while the
/// calling thread holds the segment's query pin, and nothing cached outlives the segment it was
/// read from (ADR011, ADR027).
/// </remarks>
internal sealed class SpatialDocumentValues
{
    private readonly Dictionary<string, FieldValues> _fields = new(StringComparer.Ordinal);
    private SegmentReader? _reader;
    private int _segmentVersion;

    /// <summary>Gets the reader of the current document.</summary>
    public SegmentReader Reader => _reader ?? throw new InvalidOperationException("No current spatial document.");

    /// <summary>Gets the segment-local identifier of the current document.</summary>
    public int DocumentId { get; private set; } = -1;

    /// <summary>Moves to a document; a different reader starts a new segment.</summary>
    public void MoveTo(SegmentReader reader, int documentId)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (!ReferenceEquals(reader, _reader))
        {
            _reader = reader;
            _segmentVersion++;
        }

        DocumentId = documentId;
    }

    /// <summary>Returns whether <paramref name="field"/> is a Geo shape field in the current segment.</summary>
    public bool IsGeoShapeField(string field) => GetField(field).IsGeoShapeField;

    /// <summary>Gets the current document's shape record metadata, reading it at most once.</summary>
    public bool TryGetShapeRecord(string field, out ShapeDocValuesRecordMetadata record)
    {
        FieldValues values = GetField(field);
        if (values.RecordDocumentId != DocumentId)
        {
            values.HasRecord = Reader.TryGetShapeDocValuesRecordMetadata(field, DocumentId, out values.Record);
            values.RecordDocumentId = DocumentId;
        }

        record = values.Record;
        return values.HasRecord;
    }

    /// <summary>Visits the current document's shape primitives using record metadata already read.</summary>
    public int VisitShapePrimitives(string field, in ShapeDocValuesRecordMetadata record, Action<ShapePrimitive> visitor)
        => Reader.VisitShapeDocValuesPrimitives(field, record, visitor);

    /// <summary>
    /// Gets the current document's Geo points, decoded at most once: exact point DocValues when the
    /// document has them, otherwise the legacy latitude and longitude fields.
    /// </summary>
    /// <exception cref="InvalidDataException">The document's point values are malformed.</exception>
    public bool TryGetPoints(string field, out ReadOnlySpan<GeoPoint> points)
    {
        FieldValues values = GetField(field);
        if (values.PointsDocumentId != DocumentId)
        {
            values.PointCount = ReadPoints(field, values);
            values.PointsDocumentId = DocumentId;
        }

        points = values.PointCount < 0 ? [] : values.Points.AsSpan(0, values.PointCount);
        return values.PointCount >= 0;
    }

    // Returns the number of points, or -1 when the document has none.
    private int ReadPoints(string field, FieldValues values)
    {
        SegmentReader reader = Reader;
        if (values.ExactColumn is { } column)
        {
            int count = column.GetValueRange(DocumentId, out int firstValue);
            if (count > 0)
            {
                EnsurePointCapacity(values, count);
                Span<byte> buffer = stackalloc byte[GeoPointDocValues.ValueLength];
                for (int i = 0; i < count; i++)
                {
                    int length = column.CopyValue(firstValue + i, buffer);
                    if (length != GeoPointDocValues.ValueLength
                        || !GeoPointDocValues.TryDecode(buffer, out double latitude, out double longitude))
                        throw Malformed(field);
                    values.Points[i] = new GeoPoint(latitude, longitude);
                }

                return count;
            }
        }
        else if (!values.ExactColumnPinned
            && reader.TryGetBinaryDocValues(values.ExactFieldName, DocumentId, out IReadOnlyList<byte[]> exact))
        {
            EnsurePointCapacity(values, exact.Count);
            for (int i = 0; i < exact.Count; i++)
            {
                if (!GeoPointDocValues.TryDecode(exact[i], out double latitude, out double longitude))
                    throw Malformed(field);
                values.Points[i] = new GeoPoint(latitude, longitude);
            }

            return exact.Count;
        }

        bool hasLatitude = reader.TryGetNumericValue(values.LatitudeFieldName, DocumentId, out double legacyLatitude);
        bool hasLongitude = reader.TryGetNumericValue(values.LongitudeFieldName, DocumentId, out double legacyLongitude);
        if (hasLatitude != hasLongitude)
            throw new InvalidDataException($"Legacy Geo point field '{field}' has only one coordinate for document {DocumentId}.");
        if (!hasLatitude)
            return -1;

        EnsurePointCapacity(values, 1);
        values.Points[0] = new GeoPoint(legacyLatitude, legacyLongitude);
        return 1;
    }

    private InvalidDataException Malformed(string field)
        => new($"Geo point DocValues for field '{field}' and document {DocumentId} are malformed.");

    private static void EnsurePointCapacity(FieldValues values, int count)
    {
        if (values.Points.Length < count)
            values.Points = new GeoPoint[Math.Max(count, values.Points.Length * 2)];
    }

    private FieldValues GetField(string field)
    {
        if (!_fields.TryGetValue(field, out FieldValues? values))
        {
            values = new FieldValues(field);
            _fields.Add(field, values);
        }

        if (values.SegmentVersion != _segmentVersion)
            values.ResolveSegment(Reader, _segmentVersion);
        return values;
    }

    private sealed class FieldValues(string field)
    {
        public string ExactFieldName { get; } = GeoPointDocValues.GetFieldName(field);
        public string LatitudeFieldName { get; } = field + "_lat";
        public string LongitudeFieldName { get; } = field + "_lon";
        public int SegmentVersion { get; private set; } = -1;
        public bool IsGeoShapeField { get; private set; }
        public BinaryDocValuesColumn? ExactColumn { get; private set; }
        public bool ExactColumnPinned { get; private set; }

        public int RecordDocumentId = -1;
        public bool HasRecord;
        public ShapeDocValuesRecordMetadata Record;

        public int PointsDocumentId = -1;
        public int PointCount = -1;
        public GeoPoint[] Points = new GeoPoint[1];

        public void ResolveSegment(SegmentReader reader, int segmentVersion)
        {
            IsGeoShapeField = SpatialAggregationValidation.IsGeoShapeField(reader, field);
            // Without a query pin the column cannot be held, so reads fall back to the copying facade.
            reader.TryGetBinaryDocValuesColumnIfPinned(ExactFieldName, out BinaryDocValuesColumn? column, out bool pinned);
            ExactColumnPinned = pinned;
            ExactColumn = column;
            RecordDocumentId = -1;
            PointsDocumentId = -1;
            SegmentVersion = segmentVersion;
        }
    }
}
