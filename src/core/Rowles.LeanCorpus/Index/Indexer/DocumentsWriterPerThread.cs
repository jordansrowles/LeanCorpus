using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// A per-thread document buffer for concurrent indexing.
/// Each thread accumulates postings independently; the writer merges them on flush.
/// </summary>
internal sealed class DocumentsWriterPerThread
{
    private readonly IAnalyser _analyser;
    private readonly Dictionary<string, IAnalyser> _fieldAnalysers;
    private readonly IndexWriterConfig _config;
    internal readonly Dictionary<string, PostingAccumulator> Postings = new(StringComparer.Ordinal);

    /// <summary>Stored-field name-to-ID mapping exposed for <see cref="SegmentFlusher.FlushFromDwpt"/>.</summary>
    internal Dictionary<string, int> StoredFieldNameToId => _storedFieldNameToId;

    /// <summary>Always <see langword="null"/> — block-join indexing is not supported on the concurrent path.</summary>
    internal HashSet<int>? ParentDocIds => null;

    /// <summary>Looks up or creates a posting accumulator by qualified term.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal PostingAccumulator GetOrCreateAccumulator(string qualifiedTerm)
    {
        if (!Postings.TryGetValue(qualifiedTerm, out var acc))
        {
            acc = new PostingAccumulator();
            Postings[qualifiedTerm] = acc;
            _estimatedRamBytes += qualifiedTerm.Length * 2 + 128;
        }
        return acc;
    }

    // Stored fields as a flat struct-of-arrays buffer (mirrors the main writer).
    // StoredDocStarts[d] = start index into StoredFieldIds/StoredValues for doc d.
    internal readonly List<int> StoredDocStarts = [];
    internal readonly List<int> StoredFieldIds = [];
    internal readonly List<StoredFieldValue> StoredValues = [];
    internal readonly List<string> StoredFieldIdToName = [];
    private readonly Dictionary<string, int> _storedFieldNameToId = new(StringComparer.Ordinal);

    internal readonly Dictionary<string, Dictionary<int, double>> NumericIndex = new();
    internal readonly Dictionary<string, Dictionary<int, long>> Int64Index = new();
    internal readonly Dictionary<string, List<double>> NumericDocValues = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, List<long>> Int64DocValues = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, List<string?>> SortedDocValues = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Dictionary<int, List<string>>> SortedSetDocValues = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Dictionary<int, List<double>>> SortedNumericDocValues = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Dictionary<int, List<long>>> Int64SortedDocValues = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Dictionary<int, List<byte[]>>> BinaryDocValues = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>> Vectors = new(StringComparer.Ordinal);
    internal readonly HashSet<string> FieldNames = new(StringComparer.Ordinal);
    // Per-field token counts: field → docId → count
    internal Dictionary<string, int[]> DocTokenCounts = new(StringComparer.Ordinal);
    internal Dictionary<string, Dictionary<int, float>> FieldBoosts = new(StringComparer.Ordinal);
    internal int DocCount;
    private readonly Dictionary<string, string> _qualifiedTermPool = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fieldPrefixCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _termPool = new(StringComparer.Ordinal);
    private readonly SpanPostingTokenSink _spanPostingSink;
    private long _estimatedRamBytes;

    /// <summary>Estimated RAM usage in bytes for this DWPT's buffers.</summary>
    public long EstimatedRamBytes => Volatile.Read(ref _estimatedRamBytes);

    public DocumentsWriterPerThread(IAnalyser defaultAnalyser, Dictionary<string, IAnalyser> fieldAnalysers, IndexWriterConfig config)
    {
        _analyser = defaultAnalyser;
        _fieldAnalysers = fieldAnalysers;
        _config = config;
        _spanPostingSink = new SpanPostingTokenSink(this);
    }

    /// <summary>Resets all buffers to empty state for reuse.</summary>
    internal void ClearAll()
    {
        Postings.Clear();
        StoredDocStarts.Clear();
        StoredFieldIds.Clear();
        StoredValues.Clear();
        StoredFieldIdToName.Clear();
        _storedFieldNameToId.Clear();
        NumericIndex.Clear();
        Int64Index.Clear();
        NumericDocValues.Clear();
        Int64DocValues.Clear();
        SortedDocValues.Clear();
        SortedSetDocValues.Clear();
        SortedNumericDocValues.Clear();
        Int64SortedDocValues.Clear();
        BinaryDocValues.Clear();
        Vectors.Clear();
        FieldNames.Clear();
        DocTokenCounts.Clear();
        FieldBoosts.Clear();
        _qualifiedTermPool.Clear();
        _fieldPrefixCache.Clear();
        _termPool.Clear();
        DocCount = 0;
        _estimatedRamBytes = 0;
    }

    /// <summary>
    /// Indexes a single document into this thread's local buffer.
    /// Not thread-safe — each thread owns its own DWPT instance.
    /// </summary>
    public void AddDocument(LeanDocument doc)
    {
        int localDocId = DocCount;
        StoredDocStarts.Add(StoredFieldIds.Count);

        foreach (var field in doc.Fields)
        {
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
                    IndexStringField(sf.Name, sf.Value, localDocId, sf.StoreDocValues);
                    if (sf.IsStored)
                    {
                        AppendStored(sf.Name, StoredFieldValue.FromString(sf.Value), storeDocValues: sf.StoreDocValues);
                        _estimatedRamBytes += sf.Value.Length * 2 + 64;
                    }
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
                case VectorField vf:
                    TrackFieldBoost(vf.Name, localDocId, vf.Boost);
                    IndexVectorField(vf.Name, vf.Value, localDocId);
                    break;
                case GeoPointField gf:
                    TrackFieldBoost(gf.Name, localDocId, gf.Boost);
                    IndexNumericField(gf.LatFieldName, gf.Latitude, localDocId, gf.StoreDocValues);
                    IndexNumericField(gf.LonFieldName, gf.Longitude, localDocId, gf.StoreDocValues);
                    if (gf.IsStored)
                    {
                        AppendStored(gf.Name, StoredFieldValue.FromString(gf.Value), storeDocValues: gf.StoreDocValues);
                        _estimatedRamBytes += gf.Value.Length * 2 + 64;
                    }
                    break;
            }
        }

        DocCount++;
        _estimatedRamBytes += 32; // per-doc overhead
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
        string? filtered = null;
        if (_config.CharFilters.Count > 0)
        {
            filtered = value;
            foreach (var cf in _config.CharFilters)
                filtered = cf.Filter(filtered.AsSpan());
            input = filtered.AsSpan();
        }
        var analyser = _fieldAnalysers.GetValueOrDefault(fieldName, _analyser);
        _spanPostingSink.Reset(fieldName, docId, indexOptions);
        analyser.Analyse(input, _spanPostingSink);
        AddTokenCount(fieldName, docId, _spanPostingSink.AcceptedCount);
        FieldNames.Add(fieldName);
    }


    private void AddTokenCount(string fieldName, int docId, int tokenCount)
    {
        if (!DocTokenCounts.TryGetValue(fieldName, out var counts))
        {
            counts = new int[16];
            DocTokenCounts[fieldName] = counts;
        }
        if (docId >= counts.Length)
            Array.Resize(ref counts, Math.Max(counts.Length * 2, docId + 1));
        counts[docId] += tokenCount;
        DocTokenCounts[fieldName] = counts; // Update reference in case of resize
    }

    private void IndexStringField(string fieldName, string value, int docId, bool storeDocValues = true)
    {
        FieldNames.Add(fieldName);
        var term = CanonicaliseTerm(value);
        var qualifiedTerm = GetOrCreateQualifiedTerm(fieldName, term);

        if (!Postings.TryGetValue(qualifiedTerm, out var acc))
        {
            acc = new PostingAccumulator();
            Postings[qualifiedTerm] = acc;
        }
        acc.AddDocOnly(docId);

        if (storeDocValues)
        {
            // Populate sorted DV column so collapse/facet behave the same as on the main writer.
            if (!SortedDocValues.TryGetValue(fieldName, out var dvList))
            {
                dvList = new List<string?>();
                SortedDocValues[fieldName] = dvList;
            }
            while (dvList.Count <= docId) dvList.Add(null);
            dvList[docId] = value;
            AddSortedSetDocValue(fieldName, docId, value);
        }
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
        perField[docId] = value;
        _estimatedRamBytes += value.Length * sizeof(float) + 32;
    }

    private string CanonicaliseTerm(string term)
    {
        if (_termPool.TryGetValue(term, out var canonical))
            return canonical;
        _termPool.Add(term);
        return term;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetOrCreateQualifiedTerm(string fieldName, string term)
        => GetOrCreateQualifiedTerm(fieldName, term.AsSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetOrCreateQualifiedTerm(string fieldName, ReadOnlySpan<char> term)
    {
        if (!_fieldPrefixCache.TryGetValue(fieldName, out var prefix))
        {
            prefix = string.Concat(fieldName, "\x00");
            _fieldPrefixCache[fieldName] = prefix;
        }

        int totalLen = prefix.Length + term.Length;
        Span<char> buf = totalLen <= 256 ? stackalloc char[totalLen] : new char[totalLen];
        prefix.AsSpan().CopyTo(buf);
        term.CopyTo(buf[prefix.Length..]);

        var lookup = _qualifiedTermPool.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(buf, out var pooled))
            return pooled;

        var qualifiedTerm = new string(buf);
        _qualifiedTermPool[qualifiedTerm] = qualifiedTerm;
        return qualifiedTerm;
    }

    private sealed class SpanPostingTokenSink : ISpanTokenSink
    {
        private readonly DocumentsWriterPerThread _owner;
        private string _fieldName = string.Empty;
        private int _docId;
        private int _position;
        private FieldIndexOptions _fieldIndexOptions;

        public SpanPostingTokenSink(DocumentsWriterPerThread owner)
        {
            _owner = owner;
        }

        public int AcceptedCount { get; private set; }

        public void Reset(string fieldName, int docId, FieldIndexOptions indexOptions)
        {
            _fieldName = fieldName;
            _docId = docId;
            _position = -1;
            _fieldIndexOptions = indexOptions;
            AcceptedCount = 0;
        }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            int increment = positionIncrement > 0 ? positionIncrement : 0;
            if (_position < 0 && increment == 0)
                increment = 1;
            _position += increment;

            var qualifiedTerm = _owner.GetOrCreateQualifiedTerm(_fieldName, text);

            var acc = _owner.GetOrCreateAccumulator(qualifiedTerm);
            if (_owner._config.StorePayloads && (acc.HasPayloads || payload is { Length: > 0 }))
            {
                if (_owner._config.StoreTermVectors)
                    acc.AddWithPayload(_docId, _position, payload, _fieldIndexOptions, startOffset, endOffset);
                else
                    acc.AddWithPayload(_docId, _position, payload, _fieldIndexOptions);
                _owner._estimatedRamBytes += 12 + (payload?.Length ?? 0);
            }
            else
            {
                if (_owner._config.StoreTermVectors)
                    acc.Add(_docId, _position, _fieldIndexOptions, startOffset, endOffset);
                else
                    acc.Add(_docId, _position, _fieldIndexOptions);
                _owner._estimatedRamBytes += 12;
            }
            AcceptedCount++;
        }
    }
}