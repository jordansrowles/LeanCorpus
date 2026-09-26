using System.Buffers;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer.Postings;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// A per-thread document buffer for concurrent indexing.
/// Each thread accumulates postings independently; the writer merges them on flush.
/// </summary>
internal sealed class DocumentsWriterPerThread : IDisposable
{
    private readonly IAnalyser _analyser;
    private readonly Dictionary<string, IAnalyser> _fieldAnalysers;
    private readonly IndexWriterConfig _config;
    private readonly ArrayPool<PostingTermState> _postingsStatePool;
    private readonly ArrayPool<byte> _postingsBytePool;
    private readonly ArrayPool<byte> _shapePrimitivePool;
    internal PostingsStore Postings { get; private set; }

    /// <summary>Stored-field name-to-ID mapping transferred to a detached flush snapshot.</summary>
    internal Dictionary<string, int> StoredFieldNameToId => _storedFieldNameToId;

    internal HashSet<int>? ParentDocIds;

    // Stored fields as a flat struct-of-arrays buffer (mirrors the main writer).
    // StoredDocStarts[d] = start index into StoredFieldIds/StoredValues for doc d.
    internal List<int> StoredDocStarts = [];
    internal List<int> StoredFieldIds = [];
    internal List<StoredFieldValue> StoredValues = [];
    internal List<string> StoredFieldIdToName = [];
    private Dictionary<string, int> _storedFieldNameToId = new(StringComparer.Ordinal);

    internal Dictionary<string, Dictionary<int, double>> NumericIndex = new();
    internal Dictionary<string, Dictionary<int, long>> Int64Index = new();
    internal Dictionary<string, List<double>> NumericDocValues = new(StringComparer.Ordinal);
    internal Dictionary<string, List<long>> Int64DocValues = new(StringComparer.Ordinal);
    internal Dictionary<string, List<string?>> SortedDocValues = new(StringComparer.Ordinal);
    internal Dictionary<string, Dictionary<int, List<string>>> SortedSetDocValues = new(StringComparer.Ordinal);
    internal Dictionary<string, Dictionary<int, List<double>>> SortedNumericDocValues = new(StringComparer.Ordinal);
    internal Dictionary<string, Dictionary<int, List<long>>> Int64SortedDocValues = new(StringComparer.Ordinal);
    internal Dictionary<string, Dictionary<int, List<byte[]>>> BinaryDocValues = new(StringComparer.Ordinal);
    internal Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>> Vectors = new(StringComparer.Ordinal);
    internal Dictionary<string, PackedBkdFieldBuffer> PackedBkdFields = new(StringComparer.Ordinal);
    internal Dictionary<string, ShapeDocValuesFieldBuffer> ShapeDocValuesFields = new(StringComparer.Ordinal);
    internal Dictionary<string, SpatialFieldKind> SpatialFieldKinds = new(StringComparer.Ordinal);
    internal HashSet<string> FieldNames = new(StringComparer.Ordinal);
    // Per-field token counts: field → docId → count
    internal Dictionary<string, int[]> DocTokenCounts = new(StringComparer.Ordinal);
    internal Dictionary<string, Dictionary<int, float>> FieldBoosts = new(StringComparer.Ordinal);
    internal int DocCount;
    private readonly SpanPostingTokenSink _spanPostingSink;
    private readonly CountingTokenSink _countingTokenSink = new();
    private long _estimatedRamBytes;
    private long _packedBkdAllocatedBytes;
    private long _preparedSpatialAllocatedBytes;
    private long _shapeDocValuesAllocatedBytes;
    internal long PreparedSpatialAllocatedBytes => Volatile.Read(ref _preparedSpatialAllocatedBytes);
    internal long ShapeDocValuesAllocatedBytes => Volatile.Read(ref _shapeDocValuesAllocatedBytes);

    /// <summary>Estimated RAM usage in bytes for this DWPT's buffers.</summary>
    public long EstimatedRamBytes => Volatile.Read(ref _estimatedRamBytes)
        + Postings.AllocatedBytes
        + Volatile.Read(ref _packedBkdAllocatedBytes)
        + Volatile.Read(ref _preparedSpatialAllocatedBytes)
        + Volatile.Read(ref _shapeDocValuesAllocatedBytes);

    public DocumentsWriterPerThread(IAnalyser defaultAnalyser, Dictionary<string, IAnalyser> fieldAnalysers, IndexWriterConfig config)
        : this(defaultAnalyser, fieldAnalysers, config,
            ArrayPool<PostingTermState>.Shared, ArrayPool<byte>.Shared, ArrayPool<byte>.Shared)
    {
    }

    internal DocumentsWriterPerThread(
        IAnalyser defaultAnalyser,
        Dictionary<string, IAnalyser> fieldAnalysers,
        IndexWriterConfig config,
        ArrayPool<PostingTermState> postingsStatePool,
        ArrayPool<byte> postingsBytePool,
        ArrayPool<byte>? shapePrimitivePool = null)
    {
        ArgumentNullException.ThrowIfNull(defaultAnalyser);
        ArgumentNullException.ThrowIfNull(fieldAnalysers);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(postingsStatePool);
        ArgumentNullException.ThrowIfNull(postingsBytePool);
        _analyser = defaultAnalyser;
        _fieldAnalysers = fieldAnalysers;
        _config = config;
        _postingsStatePool = postingsStatePool;
        _postingsBytePool = postingsBytePool;
        _shapePrimitivePool = shapePrimitivePool ?? ArrayPool<byte>.Shared;
        Postings = new PostingsStore(
            config.StorePayloads,
            config.StoreTermVectors,
            _postingsStatePool,
            _postingsBytePool);
        _spanPostingSink = new SpanPostingTokenSink(this);
        _estimatedRamBytes = 0;
    }

    /// <summary>Resets all buffers to empty state for reuse.</summary>
    internal void ClearAll()
    {
        Postings.Dispose();
        DisposePackedBkdFields();
        DisposeShapeDocValuesFields();
        ResetAfterSnapshot();
    }

    /// <summary>
    /// Resets all mutable collections to fresh instances. Caller must have already
    /// taken ownership of the previous collections via <see cref="DwptFlushSnapshot.CaptureFrom"/>.
    /// </summary>
    internal void ResetAfterSnapshot()
    {
        Postings = new PostingsStore(
            _config.StorePayloads,
            _config.StoreTermVectors,
            _postingsStatePool,
            _postingsBytePool);
        StoredDocStarts = [];
        StoredFieldIds = [];
        StoredValues = [];
        StoredFieldIdToName = [];
        _storedFieldNameToId = new(StringComparer.Ordinal);
        NumericIndex = new();
        Int64Index = new();
        NumericDocValues = new(StringComparer.Ordinal);
        Int64DocValues = new(StringComparer.Ordinal);
        SortedDocValues = new(StringComparer.Ordinal);
        SortedSetDocValues = new(StringComparer.Ordinal);
        SortedNumericDocValues = new(StringComparer.Ordinal);
        Int64SortedDocValues = new(StringComparer.Ordinal);
        BinaryDocValues = new(StringComparer.Ordinal);
        Vectors = new(StringComparer.Ordinal);
        PackedBkdFields = new(StringComparer.Ordinal);
        ShapeDocValuesFields = new(StringComparer.Ordinal);
        SpatialFieldKinds = new(StringComparer.Ordinal);
        FieldNames = new(StringComparer.Ordinal);
        DocTokenCounts = new(StringComparer.Ordinal);
        FieldBoosts = new(StringComparer.Ordinal);
        ParentDocIds = null;
        DocCount = 0;
        _estimatedRamBytes = 0;
        Volatile.Write(ref _packedBkdAllocatedBytes, 0);
        Volatile.Write(ref _preparedSpatialAllocatedBytes, 0);
        Volatile.Write(ref _shapeDocValuesAllocatedBytes, 0);
    }

    public void Dispose()
    {
        Postings.Dispose();
        DisposePackedBkdFields();
        DisposeShapeDocValuesFields();
    }

    /// <summary>Adds one already encoded packed BKD value to a DWPT-owned field buffer.</summary>
    internal void AddPackedBkdValue(string fieldName, PackedBkdConfig config, ReadOnlySpan<byte> packedValue, int docId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        if (!PackedBkdFields.TryGetValue(fieldName, out var buffer))
        {
            buffer = new PackedBkdFieldBuffer(config);
            PackedBkdFields.Add(fieldName, buffer);
            FieldNames.Add(fieldName);
            Interlocked.Add(ref _packedBkdAllocatedBytes, buffer.AllocatedBytes);
        }
        else if (buffer.Config != config)
        {
            throw new InvalidOperationException($"Packed BKD field '{fieldName}' was indexed with inconsistent dimensions or encoding.");
        }

        long before = buffer.AllocatedBytes;
        buffer.Append(packedValue, docId);
        long after = buffer.AllocatedBytes;
        if (after != before)
            Interlocked.Add(ref _packedBkdAllocatedBytes, after - before);
    }

    internal void RegisterSpatialField(string fieldName, SpatialFieldKind kind)
    {
        if (SpatialFieldKinds.TryGetValue(fieldName, out SpatialFieldKind existing))
        {
            if (existing != kind)
                throw new InvalidOperationException($"Spatial field '{fieldName}' was registered as both '{existing}' and '{kind}'.");
            return;
        }

        SpatialFieldKinds.Add(fieldName, kind);
        _estimatedRamBytes += checked((fieldName.Length * 2L) + 48);
    }

    private void OnPreparedSpatialCapacityChanged(long delta)
    {
        Interlocked.Add(ref _preparedSpatialAllocatedBytes, delta);
        if (delta <= 0 || _config.RamPerThreadHardLimitMB <= 0)
            return;

        long hardLimit = (long)(_config.RamPerThreadHardLimitMB * 1024 * 1024);
        if (EstimatedRamBytes <= hardLimit)
            return;

        Interlocked.Add(ref _preparedSpatialAllocatedBytes, -delta);
        throw new InvalidOperationException(
            $"Prepared spatial values would exceed the per-thread RAM hard limit of {_config.RamPerThreadHardLimitMB} MB.");
    }

    private static uint NextShapeValueOrdinal(ref Dictionary<string, uint>? ordinals, string fieldName)
    {
        ordinals ??= new Dictionary<string, uint>(StringComparer.Ordinal);
        if (!ordinals.TryGetValue(fieldName, out uint next))
        {
            ordinals.Add(fieldName, 1);
            return 0;
        }

        if (next > ShapePrimitiveCodec.MaximumValueOrdinal)
            throw new ArgumentOutOfRangeException(nameof(fieldName), "A document contains more shape values than the 26-bit ordinal can represent.");
        ordinals[fieldName] = next + 1;
        return next;
    }

    private void IndexPreparedShape(
        string fieldName,
        SpatialFieldKind fieldKind,
        ReadOnlyMemory<byte> packedPrimitives,
        int docId)
    {
        ReadOnlySpan<byte> values = packedPrimitives.Span;
        if (values.Length == 0 || values.Length % ShapePrimitiveCodec.PackedValueLength != 0)
            throw new InvalidDataException("Prepared shape primitive bytes are empty or have an invalid length.");

        PackedBkdConfig config = PackedBkdConfig.Shape7D4Indexed(_config.BKDMaxLeafSize);
        for (int offset = 0; offset < values.Length; offset += ShapePrimitiveCodec.PackedValueLength)
        {
            ReadOnlySpan<byte> packedValue = values.Slice(offset, ShapePrimitiveCodec.PackedValueLength);
            ShapePrimitiveCodec.Decode(packedValue, fieldKind);
            AddPackedBkdValue(fieldName, config, packedValue, docId);
        }
    }

    private void DisposePackedBkdFields()
    {
        foreach (var buffer in PackedBkdFields.Values)
            buffer.Dispose();
        PackedBkdFields.Clear();
        Volatile.Write(ref _packedBkdAllocatedBytes, 0);
    }

    private void DisposeShapeDocValuesFields()
    {
        foreach (ShapeDocValuesFieldBuffer buffer in ShapeDocValuesFields.Values)
            buffer.Dispose();
        ShapeDocValuesFields.Clear();
        Volatile.Write(ref _shapeDocValuesAllocatedBytes, 0);
    }

    private void AddShapeDocValuesValue(
        PreparedShapeValue prepared,
        int documentId,
        ReadOnlySpan<byte> packedPrimitives)
    {
        if (!prepared.StoreDocValues)
            return;

        if (!ShapeDocValuesFields.TryGetValue(prepared.FieldName, out ShapeDocValuesFieldBuffer? buffer))
        {
            buffer = new ShapeDocValuesFieldBuffer(prepared.FieldName, prepared.FieldKind);
            ShapeDocValuesFields.Add(prepared.FieldName, buffer);
        }
        else if (buffer.Kind != prepared.FieldKind)
        {
            throw new InvalidOperationException(
                $"Shape DocValues field '{prepared.FieldName}' was indexed with inconsistent coordinate systems.");
        }

        long before = buffer.AllocatedBytes;
        buffer.AppendValue(documentId, prepared.ValueOrdinal, packedPrimitives);
        long after = buffer.AllocatedBytes;
        if (after != before)
            Interlocked.Add(ref _shapeDocValuesAllocatedBytes, after - before);
    }

    /// <summary>
    /// Indexes a single document into this thread's local buffer.
    /// Not thread-safe; each thread owns its own DWPT instance.
    /// </summary>
    public void AddDocument(LeanDocument doc)
    {
        ValidateDocument(doc);
        AddPrevalidatedDocument(doc, PrepareSpatialShapes(doc));
    }

    /// <summary>Checks document-local admission constraints without changing buffer state.</summary>
    internal void ValidateDocument(LeanDocument doc)
    {
        ValidateTokenBudget(doc);
        ValidateSpatialFieldKinds(doc);
    }

    private void ValidateSpatialFieldKinds(LeanDocument doc)
    {
        for (int i = 0; i < doc.Fields.Count; i++)
        {
            IField currentField = doc.Fields[i];
            if (!TryGetSpatialKind(currentField, out SpatialFieldKind kind))
                continue;

            string fieldName = currentField.Name;
            if (SpatialFieldKinds.TryGetValue(fieldName, out SpatialFieldKind registered) && registered != kind)
                throw new InvalidOperationException($"Spatial field '{fieldName}' was registered as both '{registered}' and '{kind}'.");

            for (int j = 0; j < i; j++)
            {
                if (doc.Fields[j].Name == fieldName
                    && TryGetSpatialKind(doc.Fields[j], out SpatialFieldKind earlierKind)
                    && earlierKind != kind)
                    throw new InvalidOperationException($"Spatial field '{fieldName}' was registered as both '{earlierKind}' and '{kind}'.");
            }

            if (TryGetShapeDocValues(currentField, out bool storeDocValues))
            {
                for (int j = 0; j < i; j++)
                {
                    IField earlierField = doc.Fields[j];
                    if (earlierField.Name == fieldName
                        && TryGetShapeDocValues(earlierField, out bool earlierStoreDocValues)
                        && earlierStoreDocValues != storeDocValues)
                        throw new InvalidOperationException(
                            $"Shape field '{fieldName}' uses conflicting StoreDocValues settings within one document.");
                }
            }
        }
    }

    private static bool TryGetShapeDocValues(IField field, out bool storeDocValues)
    {
        switch (field)
        {
            case LatLonShapeField geoShape:
                storeDocValues = geoShape.StoreDocValues;
                return true;
            case XYShapeField xyShape:
                storeDocValues = xyShape.StoreDocValues;
                return true;
            default:
                storeDocValues = false;
                return false;
        }
    }

    private static bool TryGetSpatialKind(IField field, out SpatialFieldKind kind)
    {
        switch (field)
        {
            case GeoPointField:
                kind = SpatialFieldKind.GeoPoint;
                return true;
            case XYPointField:
                kind = SpatialFieldKind.XYPoint;
                return true;
            case LatLonShapeField:
                kind = SpatialFieldKind.GeoShape;
                return true;
            case XYShapeField:
                kind = SpatialFieldKind.XYShape;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>Adds a document after the writer and DWPT admission checks have completed.</summary>
    internal void AddPrevalidatedDocument(LeanDocument doc)
        => AddPrevalidatedDocument(doc, PrepareSpatialShapes(doc));

    internal void AddPrevalidatedDocument(
        LeanDocument doc,
        PreparedSpatialDocument? preparedSpatialShapes)
    {
        try
        {
            AddDocumentCore(doc, preparedSpatialShapes);
        }
        finally
        {
            preparedSpatialShapes?.Dispose();
        }
    }

    /// <summary>Prepares every shape value before document buffers are mutated.</summary>
    internal PreparedSpatialDocument? PrepareSpatialShapes(LeanDocument doc)
    {
        PreparedSpatialDocument? prepared = null;
        Dictionary<string, uint>? ordinals = null;
        try
        {
            for (int fieldIndex = 0; fieldIndex < doc.Fields.Count; fieldIndex++)
            {
                switch (doc.Fields[fieldIndex])
                {
                    case LatLonShapeField geoShape:
                    {
                        uint ordinal = NextShapeValueOrdinal(ref ordinals, geoShape.Name);
                        prepared ??= new PreparedSpatialDocument(OnPreparedSpatialCapacityChanged, _shapePrimitivePool);
                        EncodedShapePrimitiveSink sink = prepared.CreateSink(SpatialFieldKind.GeoShape);
                        ShapeTessellator.TessellateGeo(geoShape.Geometry, ordinal, sink);
                        prepared.AddValue(fieldIndex, geoShape.Name, SpatialFieldKind.GeoShape, ordinal, geoShape.StoreDocValues, sink);
                        break;
                    }
                    case XYShapeField xyShape:
                    {
                        uint ordinal = NextShapeValueOrdinal(ref ordinals, xyShape.Name);
                        prepared ??= new PreparedSpatialDocument(OnPreparedSpatialCapacityChanged, _shapePrimitivePool);
                        EncodedShapePrimitiveSink sink = prepared.CreateSink(SpatialFieldKind.XYShape);
                        ShapeTessellator.TessellateXY(xyShape.Geometry, ordinal, sink);
                        prepared.AddValue(fieldIndex, xyShape.Name, SpatialFieldKind.XYShape, ordinal, xyShape.StoreDocValues, sink);
                        break;
                    }
                }
            }

            return prepared;
        }
        catch
        {
            prepared?.Dispose();
            throw;
        }
    }

    internal PreparedSpatialDocument?[] PrepareSpatialShapes(IReadOnlyList<LeanDocument> documents)
    {
        var prepared = new PreparedSpatialDocument?[documents.Count];
        try
        {
            for (int i = 0; i < documents.Count; i++)
                prepared[i] = PrepareSpatialShapes(documents[i]);
            return prepared;
        }
        catch
        {
            foreach (PreparedSpatialDocument? item in prepared)
                item?.Dispose();
            throw;
        }
    }

    private void AddDocumentCore(
        LeanDocument doc,
        PreparedSpatialDocument? preparedSpatialShapes)
    {
        int localDocId = DocCount;
        Span<byte> packedGeoPoint = stackalloc byte[2 * PackedBkdConfig.FixedBytesPerDimension];
        Span<byte> geoPointDocValue = stackalloc byte[GeoPointDocValues.ValueLength];
        StoredDocStarts.Add(StoredFieldIds.Count);

        for (int fieldIndex = 0; fieldIndex < doc.Fields.Count; fieldIndex++)
        {
            IField field = doc.Fields[fieldIndex];
            switch (field)
            {
                case TextField tf:
                    TrackFieldBoost(tf.Name, localDocId, tf.Boost);
                    IndexTextField(tf.Name, tf.Value, localDocId, tf.IndexOptions);
                    if (tf.IsStored)
                    {
                        AppendStored(tf.Name, StoredFieldValue.FromString(tf.Value), mirrorStringToBinaryDocValues: false, storeDocValues: tf.StoreDocValues);
                        _estimatedRamBytes += tf.Value.Length * 2 + 64;
                    }
                    break;
                case StringField sf:
                    TrackFieldBoost(sf.Name, localDocId, sf.Boost);
                    IndexStringField(sf.Name, sf.Value, localDocId, sf.DocValues);
                    if (sf.IsStored)
                    {
                        AppendStored(sf.Name, StoredFieldValue.FromString(sf.Value), storeDocValues: false);
                        _estimatedRamBytes += sf.Value.Length * 2 + 64;
                    }
                    if ((sf.DocValues & StringDocValues.Binary) != 0)
                        AddBinaryDocValue(sf.Name, localDocId, sf.Value);
                    break;
                case NumericField nf:
                    TrackFieldBoost(nf.Name, localDocId, nf.Boost);
                    IndexNumericField(nf.Name, nf.Value, localDocId, nf.StoreDocValues);
                    if (nf.IsStored)
                    {
                        var storedValue = nf.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        AppendStored(nf.Name, StoredFieldValue.FromString(storedValue), storeDocValues: nf.StoreDocValues);
                        _estimatedRamBytes += 48;
                    }
                    break;
                case Int64Field lf:
                    TrackFieldBoost(lf.Name, localDocId, lf.Boost);
                    IndexInt64Field(lf.Name, lf.Value, localDocId, lf.StoreDocValues);
                    if (lf.IsStored)
                    {
                        var storedValue = lf.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        AppendStored(lf.Name, StoredFieldValue.FromString(storedValue), storeDocValues: lf.StoreDocValues);
                        _estimatedRamBytes += 48;
                    }
                    break;
                case StoredField sf:
                    AppendStored(sf.Name, StoredFieldValue.FromString(sf.Value), storeDocValues: sf.StoreDocValues);
                    _estimatedRamBytes += sf.Value.Length * 2 + 64;
                    break;
                case BinaryField bf:
                    AppendStored(bf.Name, StoredFieldValue.FromBinary(bf.Value.Span), storeDocValues: bf.StoreDocValues);
                    _estimatedRamBytes += bf.Value.Length + 64;
                    break;
                case InetAddressField ipf:
                    AppendStored(
                        ipf.Name,
                        StoredFieldValue.FromBinary(ipf.Value.Span),
                        storeDocValues: ipf.StoreDocValues);
                    _estimatedRamBytes += ipf.Value.Length + 64;
                    break;
                case VectorField vf:
                    TrackFieldBoost(vf.Name, localDocId, vf.Boost);
                    IndexVectorField(vf.Name, vf.Value, localDocId);
                    break;
                case GeoPointField gf:
                    RegisterSpatialField(gf.Name, SpatialFieldKind.GeoPoint);
                    TrackFieldBoost(gf.Name, localDocId, gf.Boost);
                    GeoEncodingUtils.WriteLonSortable(gf.Longitude, packedGeoPoint);
                    GeoEncodingUtils.WriteLatSortable(gf.Latitude, packedGeoPoint[PackedBkdConfig.FixedBytesPerDimension..]);
                    AddPackedBkdValue(
                        gf.Name,
                        PackedBkdConfig.Point2D(_config.BKDMaxLeafSize),
                        packedGeoPoint,
                        localDocId);
                    GeoPointDocValues.Encode(gf.Latitude, gf.Longitude, geoPointDocValue);
                    AddBinaryDocValue(
                        GeoPointDocValues.GetFieldName(gf.Name),
                        localDocId,
                        geoPointDocValue);
                    IndexNumericField(gf.LatFieldName, gf.Latitude, localDocId, gf.StoreDocValues);
                    IndexNumericField(gf.LonFieldName, gf.Longitude, localDocId, gf.StoreDocValues);
                    if (gf.IsStored)
                    {
                        AppendStored(gf.Name, StoredFieldValue.FromString(gf.Value), storeDocValues: gf.StoreDocValues);
                        _estimatedRamBytes += gf.Value.Length * 2 + 64;
                    }
                    break;
                case XYPointField xy:
                    RegisterSpatialField(xy.Name, SpatialFieldKind.XYPoint);
                    TrackFieldBoost(xy.Name, localDocId, xy.Boost);
                    XYEncodingUtils.Encode(xy.X, packedGeoPoint);
                    XYEncodingUtils.Encode(xy.Y, packedGeoPoint[PackedBkdConfig.FixedBytesPerDimension..]);
                    AddPackedBkdValue(
                        xy.Name,
                        PackedBkdConfig.Point2D(_config.BKDMaxLeafSize),
                        packedGeoPoint,
                        localDocId);
                    AddBinaryDocValue(xy.Name, localDocId, packedGeoPoint);
                    break;
                case LatLonShapeField geoShape:
                {
                    PreparedShapeValue prepared = GetPreparedSpatialShape(preparedSpatialShapes, fieldIndex);
                    RegisterSpatialField(geoShape.Name, SpatialFieldKind.GeoShape);
                    TrackFieldBoost(geoShape.Name, localDocId, geoShape.Boost);
                    ReadOnlyMemory<byte> packedPrimitives =
                        GetPreparedSpatialDocument(preparedSpatialShapes).GetPackedPrimitives(prepared);
                    IndexPreparedShape(
                        geoShape.Name,
                        SpatialFieldKind.GeoShape,
                        packedPrimitives,
                        localDocId);
                    AddShapeDocValuesValue(prepared, localDocId, packedPrimitives.Span);
                    break;
                }
                case XYShapeField xyShape:
                {
                    PreparedShapeValue prepared = GetPreparedSpatialShape(preparedSpatialShapes, fieldIndex);
                    RegisterSpatialField(xyShape.Name, SpatialFieldKind.XYShape);
                    TrackFieldBoost(xyShape.Name, localDocId, xyShape.Boost);
                    ReadOnlyMemory<byte> packedPrimitives =
                        GetPreparedSpatialDocument(preparedSpatialShapes).GetPackedPrimitives(prepared);
                    IndexPreparedShape(
                        xyShape.Name,
                        SpatialFieldKind.XYShape,
                        packedPrimitives,
                        localDocId);
                    AddShapeDocValuesValue(prepared, localDocId, packedPrimitives.Span);
                    break;
                }
            }
        }

        DocCount++;
        _estimatedRamBytes += 32; // per-doc overhead
    }

    public void AddDocumentBlock(IReadOnlyList<LeanDocument> block)
    {
        ValidateDocumentBlock(block);
        AddPrevalidatedDocumentBlock(block, PrepareSpatialShapes(block));
    }

    /// <summary>Checks every document in a block without changing buffer state.</summary>
    internal void ValidateDocumentBlock(IReadOnlyList<LeanDocument> block)
    {
        var spatialKinds = new Dictionary<string, SpatialFieldKind>(SpatialFieldKinds, StringComparer.Ordinal);
        for (int i = 0; i < block.Count; i++)
        {
            LeanDocument document = block[i];
            ValidateDocument(document);
            foreach (IField field in document.Fields)
            {
                if (!TryGetSpatialKind(field, out SpatialFieldKind kind))
                    continue;
                if (spatialKinds.TryGetValue(field.Name, out SpatialFieldKind existing) && existing != kind)
                    throw new InvalidOperationException($"Spatial field '{field.Name}' was registered as both '{existing}' and '{kind}'.");
                spatialKinds[field.Name] = kind;
            }
        }
    }

    /// <summary>Adds a block after the writer and DWPT admission checks have completed.</summary>
    internal void AddPrevalidatedDocumentBlock(IReadOnlyList<LeanDocument> block)
        => AddPrevalidatedDocumentBlock(block, PrepareSpatialShapes(block));

    internal void AddPrevalidatedDocumentBlock(
        IReadOnlyList<LeanDocument> block,
        IReadOnlyList<PreparedSpatialDocument?> preparedSpatialShapes)
    {
        try
        {
            if (preparedSpatialShapes.Count != block.Count)
                throw new ArgumentException("Prepared spatial document count does not match the document block.", nameof(preparedSpatialShapes));
            for (int i = 0; i < block.Count; i++)
            {
                AddDocumentCore(block[i], preparedSpatialShapes[i]);
                if (i == block.Count - 1)
                {
                    ParentDocIds ??= [];
                    ParentDocIds.Add(DocCount - 1);
                }
            }
        }
        finally
        {
            foreach (PreparedSpatialDocument? prepared in preparedSpatialShapes)
                prepared?.Dispose();
        }
    }

    private static PreparedShapeValue GetPreparedSpatialShape(
        PreparedSpatialDocument? preparedSpatialShapes,
        int fieldIndex)
    {
        if (preparedSpatialShapes is not null)
        {
            foreach (PreparedShapeValue prepared in preparedSpatialShapes.Values)
                if (prepared.FieldIndex == fieldIndex)
                    return prepared;
        }
        throw new InvalidDataException("A shape field reached DWPT mutation without an owned encoded primitive value.");
    }

    private static PreparedSpatialDocument GetPreparedSpatialDocument(PreparedSpatialDocument? preparedSpatialShapes)
        => preparedSpatialShapes ?? throw new InvalidDataException("A shape field reached DWPT mutation without a prepared document.");

    internal void ValidateTokenBudget(LeanDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        int budget = _config.MaxTokensPerDocument;
        if (budget <= 0 || _config.TokenBudgetPolicy != TokenBudgetPolicy.Reject)
            return;

        foreach (var field in doc.Fields)
        {
            if (field is not TextField textField)
                continue;

            ReadOnlySpan<char> input = textField.Value.AsSpan();
            CharFilterResult? filtered = null;
            if (_config.CharFilters.Count > 0)
            {
                filtered = ApplyCharFilters(textField.Value);
                input = filtered.Text.AsSpan();
            }

            var analyser = _fieldAnalysers.GetValueOrDefault(textField.Name, _analyser);
            _countingTokenSink.Reset(budget);
            analyser.Analyse(input, _countingTokenSink);
            if (_countingTokenSink.Exceeded)
                throw new TokenBudgetExceededException(_countingTokenSink.Count, budget);
        }
    }

    private void AppendStored(string name, StoredFieldValue value, bool mirrorStringToBinaryDocValues = true, bool storeDocValues = true)
    {
        if (!_storedFieldNameToId.TryGetValue(name, out int id))
        {
            id = StoredFieldIdToName.Count;
            _storedFieldNameToId[name] = id;
            StoredFieldIdToName.Add(name);
        }
        StoredFieldIds.Add(id);
        StoredValues.Add(value);
        if (storeDocValues)
        {
            if (value.IsBinary)
            {
                AddBinaryDocValue(name, DocCount, value.BinaryValue ?? []);
            }
            else if (mirrorStringToBinaryDocValues && value.StringValue is not null)
            {
                AddBinaryDocValue(name, DocCount, value.StringValue);
            }
        }
    }

    private void IndexTextField(string fieldName, string value, int docId, FieldIndexOptions indexOptions)
    {
        ReadOnlySpan<char> input = value.AsSpan();
        CharFilterResult? filtered = null;
        if (_config.CharFilters.Count > 0)
        {
            filtered = ApplyCharFilters(value);
            input = filtered.Text.AsSpan();
        }
        var analyser = _fieldAnalysers.GetValueOrDefault(fieldName, _analyser);
        int budget = _config.MaxTokensPerDocument;
        _spanPostingSink.Reset(fieldName, docId, indexOptions, budget, _config.TokenBudgetPolicy);
        if (filtered is null)
            analyser.Analyse(input, _spanPostingSink);
        else
            filtered.Analyse(analyser, _spanPostingSink);
        AddTokenCount(fieldName, docId, _spanPostingSink.AcceptedCount);
        FieldNames.Add(fieldName);
    }

    private CharFilterResult ApplyCharFilters(string source)
    {
        var filtered = new CharFilterResult(source);
        foreach (var charFilter in _config.CharFilters)
            filtered = filtered.Apply(charFilter);
        return filtered;
    }


    private void AddTokenCount(string fieldName, int docId, int tokenCount)
    {
        if (!DocTokenCounts.TryGetValue(fieldName, out var counts))
        {
            counts = new int[16];
            DocTokenCounts[fieldName] = counts;
            _estimatedRamBytes += counts.Length * sizeof(int);
        }
        if (docId >= counts.Length)
        {
            int previousLength = counts.Length;
            Array.Resize(ref counts, Math.Max(counts.Length * 2, docId + 1));
            _estimatedRamBytes += (counts.Length - previousLength) * sizeof(int);
        }
        counts[docId] += tokenCount;
        DocTokenCounts[fieldName] = counts; // Update reference in case of resize
    }

    private void IndexStringField(string fieldName, string value, int docId, StringDocValues docValues)
    {
        FieldNames.Add(fieldName);
        Postings.AddDocOnly(fieldName, value.AsSpan(), docId);

        if ((docValues & StringDocValues.Sorted) != 0)
        {
            // Populate sorted DV column so collapse/facet behave the same as on the main writer.
            if (!SortedDocValues.TryGetValue(fieldName, out var dvList))
            {
                dvList = new List<string?>();
                SortedDocValues[fieldName] = dvList;
            }
            while (dvList.Count <= docId) dvList.Add(null);
            dvList[docId] = value;
        }
        if ((docValues & StringDocValues.SortedSet) != 0)
            AddSortedSetDocValue(fieldName, docId, value);
        _estimatedRamBytes += value.Length * 2 + 16;
    }

    private void IndexNumericField(string fieldName, double value, int docId, bool storeDocValues = true)
    {
        FieldNames.Add(fieldName);
        if (!NumericIndex.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, double>();
            NumericIndex[fieldName] = fieldMap;
        }
        fieldMap[docId] = value;

        if (storeDocValues)
        {
            if (!NumericDocValues.TryGetValue(fieldName, out var dvList))
            {
                dvList = new List<double>();
                NumericDocValues[fieldName] = dvList;
            }
            while (dvList.Count <= docId) dvList.Add(0);
            dvList[docId] = value;
            AddSortedNumericDocValue(fieldName, docId, value);
        }
        _estimatedRamBytes += 24;
    }

    private void AddSortedSetDocValue(string fieldName, int docId, string value)
    {
        if (!SortedSetDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<string>>();
            SortedSetDocValues[fieldName] = fieldMap;
        }

        if (!fieldMap.TryGetValue(docId, out var values))
        {
            values = [];
            fieldMap[docId] = values;
        }

        values.Add(value);
    }

    private void AddSortedNumericDocValue(string fieldName, int docId, double value)
    {
        if (!SortedNumericDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<double>>();
            SortedNumericDocValues[fieldName] = fieldMap;
        }

        if (!fieldMap.TryGetValue(docId, out var values))
        {
            values = [];
            fieldMap[docId] = values;
        }

        values.Add(value);
    }

    private void IndexInt64Field(string fieldName, long value, int docId, bool storeDocValues = true)
    {
        FieldNames.Add(fieldName);
        if (!Int64Index.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, long>();
            Int64Index[fieldName] = fieldMap;
        }
        fieldMap[docId] = value;

        if (storeDocValues)
        {
            if (!Int64DocValues.TryGetValue(fieldName, out var dvList))
            {
                dvList = new List<long>();
                Int64DocValues[fieldName] = dvList;
            }
            while (dvList.Count <= docId) dvList.Add(0);
            dvList[docId] = value;
            AddSortedInt64DocValue(fieldName, docId, value);
        }
        _estimatedRamBytes += 24;
    }

    private void AddSortedInt64DocValue(string fieldName, int docId, long value)
    {
        if (!Int64SortedDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<long>>();
            Int64SortedDocValues[fieldName] = fieldMap;
        }

        if (!fieldMap.TryGetValue(docId, out var values))
        {
            values = [];
            fieldMap[docId] = values;
        }

        values.Add(value);
    }

    private void AddBinaryDocValue(string fieldName, int docId, string value)
    {
        AddBinaryDocValueCore(fieldName, docId, System.Text.Encoding.UTF8.GetBytes(value));
    }

    private void AddBinaryDocValue(string fieldName, int docId, ReadOnlySpan<byte> value)
    {
        AddBinaryDocValueCore(fieldName, docId, value.ToArray());
    }

    private void AddBinaryDocValueCore(string fieldName, int docId, byte[] value)
    {
        if (!BinaryDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<byte[]>>();
            BinaryDocValues[fieldName] = fieldMap;
        }

        if (!fieldMap.TryGetValue(docId, out var values))
        {
            values = [];
            fieldMap[docId] = values;
        }

        values.Add(value);
    }

    private void TrackFieldBoost(string fieldName, int docId, float boost)
    {
        if (boost == 1.0f)
            return;

        if (!FieldBoosts.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, float>();
            FieldBoosts[fieldName] = fieldMap;
        }

        if (fieldMap.TryGetValue(docId, out var existingBoost))
        {
            if (Math.Abs(existingBoost - boost) > 1e-6f)
            {
                throw new InvalidOperationException(
                    $"Document field '{fieldName}' was indexed multiple times with conflicting boosts ({existingBoost} and {boost}). Use one consistent boost per field per document.");
            }

            return;
        }

        fieldMap[docId] = boost;
    }

    private void IndexVectorField(string fieldName, ReadOnlyMemory<float> value, int docId)
    {
        FieldNames.Add(fieldName);
        if (!Vectors.TryGetValue(fieldName, out var perField))
        {
            perField = new Dictionary<int, ReadOnlyMemory<float>>();
            Vectors[fieldName] = perField;
        }
        // The writer owns accepted vector storage. Callers may reuse or mutate their
        // array after AddDocument returns without affecting buffered or persisted data.
        perField[docId] = value.ToArray();
        _estimatedRamBytes += value.Length * sizeof(float) + 32;
    }

    private sealed class SpanPostingTokenSink : ISpanTokenSink
    {
        private readonly DocumentsWriterPerThread _owner;
        private string _fieldName = string.Empty;
        private int _docId;
        private int _position;
        private FieldIndexOptions _fieldIndexOptions;
        private int _budget;
        private TokenBudgetPolicy _budgetPolicy;

        public SpanPostingTokenSink(DocumentsWriterPerThread owner)
        {
            _owner = owner;
        }

        public int AcceptedCount { get; private set; }

        public void Reset(string fieldName, int docId, FieldIndexOptions indexOptions, int budget,
            TokenBudgetPolicy budgetPolicy)
        {
            _fieldName = fieldName;
            _docId = docId;
            _position = -1;
            _fieldIndexOptions = indexOptions;
            _budget = budget;
            _budgetPolicy = budgetPolicy;
            AcceptedCount = 0;
        }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
            => Add(text, startOffset, endOffset, type, positionIncrement, 1, payload);

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type,
            int positionIncrement,
            int positionLength,
            byte[]? payload)
        {
            if (positionLength != 1)
                throw new InvalidOperationException("Index-time token graphs require FlattenGraphFilter before indexing.");
            if (_budget > 0 && _budgetPolicy == TokenBudgetPolicy.Truncate && AcceptedCount >= _budget)
                return;

            int increment = positionIncrement > 0 ? positionIncrement : 0;
            if (_position < 0 && increment == 0)
                increment = 1;
            _position += increment;

            _owner.Postings.Add(_fieldName, text, _docId, _position, _fieldIndexOptions,
                payload, startOffset, endOffset);
            AcceptedCount++;
        }
    }

    private sealed class CountingTokenSink : ISpanTokenSink
    {
        private int _limit;
        public int Count { get; private set; }
        public bool Exceeded { get; private set; }

        public void Reset(int limit)
        {
            _limit = limit;
            Count = 0;
            Exceeded = false;
        }

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
            string type = Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
        {
            Count++;
            if (Count > _limit)
                Exceeded = true;
        }

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type,
            int positionIncrement, int positionLength, byte[]? payload) => Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }
}
