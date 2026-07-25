using System.Diagnostics;
using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    private void AddDocumentCore(LeanDocument doc, bool suppressFlush = false)
    {
        using var activity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.AddDocument);
        var buffer = Buffer;
        int localDocId = buffer.DocCount;
        buffer.StoredDocStarts.Add(buffer.StoredFieldIds.Count);
        int storedEntryStart = buffer.StoredFieldIds.Count;

        foreach (var field in doc.Fields)
        {
            switch (field)
            {
                case TextField tf:
                    TrackFieldBoost(tf.Name, localDocId, tf.Boost);
                    IndexTextField(tf.Name, tf.Value, localDocId, tf.IndexOptions);
                    if (tf.IsStored)
                    {
                        AppendStoredField(tf.Name, StoredFieldValue.FromString(tf.Value), mirrorStringToBinaryDocValues: false, storeDocValues: tf.StoreDocValues);
                    }
                    break;
                case StringField sf:
                    TrackFieldBoost(sf.Name, localDocId, sf.Boost);
                    IndexStringField(sf.Name, sf.Value, localDocId, sf.DocValues);
                    if (sf.IsStored)
                    {
                        AppendStoredField(sf.Name, StoredFieldValue.FromString(sf.Value), storeDocValues: false);
                    }
                    if ((sf.DocValues & StringDocValues.Binary) != 0)
                        AddBinaryDocValue(sf.Name, localDocId, sf.Value);
                    break;
                case NumericField nf:
                    TrackFieldBoost(nf.Name, localDocId, nf.Boost);
                    IndexNumericField(nf.Name, nf.Value, localDocId, nf.StoreDocValues);
                    if (nf.IsStored)
                    {
                        AppendStoredField(nf.Name, StoredFieldValue.FromString(nf.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), storeDocValues: nf.StoreDocValues);
                    }
                    break;
                case Int64Field lf:
                    TrackFieldBoost(lf.Name, localDocId, lf.Boost);
                    IndexInt64Field(lf.Name, lf.Value, localDocId, lf.StoreDocValues);
                    if (lf.IsStored)
                    {
                        AppendStoredField(lf.Name, StoredFieldValue.FromString(lf.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), storeDocValues: lf.StoreDocValues);
                    }
                    break;
                case VectorField vf:
                    TrackFieldBoost(vf.Name, localDocId, vf.Boost);
                    if (!buffer.Vectors.TryGetValue(vf.Name, out var perField))
                    {
                        perField = new Dictionary<int, ReadOnlyMemory<float>>();
                        buffer.Vectors[vf.Name] = perField;
                    }
                    perField[localDocId] = vf.Value;
                    break;
                case GeoPointField gf:
                    TrackFieldBoost(gf.Name, localDocId, gf.Boost);
                    IndexNumericField(gf.LatFieldName, gf.Latitude, localDocId, gf.StoreDocValues);
                    IndexNumericField(gf.LonFieldName, gf.Longitude, localDocId, gf.StoreDocValues);
                    if (gf.IsStored)
                        AppendStoredField(gf.Name, StoredFieldValue.FromString(gf.Value), storeDocValues: gf.StoreDocValues);
                    break;
                case StoredField sf:
                    AppendStoredField(sf.Name, StoredFieldValue.FromString(sf.Value), storeDocValues: sf.StoreDocValues);
                    break;
                case BinaryField bf:
                    AppendStoredField(bf.Name, StoredFieldValue.FromBinary(bf.Value.Span), storeDocValues: bf.StoreDocValues);
                    break;
            }
        }

        if (Config.TrackSequenceNumbers)
        {
            if (buffer.DocCount == 0 && localDocId == 0)
                _flushSeqNoStart = _nextSequenceNumber;
            _nextSequenceNumber++;
        }

        buffer.DocCount++;
        _contentChangedSinceCommit = true;

        // Track stored-field RAM (postings tracked accurately via EstimatedBytes)
        for (int i = storedEntryStart; i < buffer.StoredFieldIds.Count; i++)
            buffer.EstimatedRamBytes += buffer.StoredFieldValues[i].EstimatedSize;

        // Check flush thresholds
        if (!suppressFlush && ShouldFlush())
            FlushSegment();
    }

    private void IndexTextField(string fieldName, string value, int docId, FieldIndexOptions indexOptions)
    {
        // Apply char filters before tokenisation
        ReadOnlySpan<char> input = value.AsSpan();
        string? filtered = null;
        if (Config.CharFilters.Count > 0)
        {
            filtered = value;
            foreach (var cf in Config.CharFilters)
                filtered = cf.Filter(filtered.AsSpan());
            input = filtered.AsSpan();
        }

        if (!_analyserCache.TryGetValue(fieldName, out var analyser))
        {
            analyser = Config.FieldAnalysers.GetValueOrDefault(fieldName, _defaultAnalyser);
            _analyserCache[fieldName] = analyser;
        }

        // Enforce token budget if configured
        int budget = Config.MaxTokensPerDocument;
        if (budget > 0 && Config.TokenBudgetPolicy == Analysis.TokenBudgetPolicy.Reject)
        {
            _spanCountingSink.Reset(limit: budget);
            analyser.Analyse(input, _spanCountingSink);
            if (_spanCountingSink.ExceededLimit)
                throw new Analysis.TokenBudgetExceededException(_spanCountingSink.Count, budget);
        }

        _spanPostingSink.Reset(fieldName, docId, budget, Config.TokenBudgetPolicy, indexOptions);
        using var analyseActivity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.Analyse);
        analyser.Analyse(input, _spanPostingSink);
        AddTokenCount(fieldName, docId, _spanPostingSink.AcceptedCount);
        Buffer.FieldNames.Add(fieldName);
    }


    private void AddTokenCount(string fieldName, int docId, int tokenCount)
    {
        if (!Buffer.DocTokenCounts.TryGetValue(fieldName, out var counts))
        {
            counts = new int[Config.MaxBufferedDocs];
            Buffer.DocTokenCounts[fieldName] = counts;
        }
        else if (docId >= counts.Length)
        {
            // Rare case: exceeded MaxBufferedDocs, grow the array
            Array.Resize(ref counts, Math.Max(counts.Length * 2, docId + 1));
            Buffer.DocTokenCounts[fieldName] = counts;
        }
        counts[docId] += tokenCount;
    }

    private void IndexStringField(string fieldName, string value, int docId, StringDocValues docValues)
    {
        Buffer.FieldNames.Add(fieldName);
        var term = Buffer.CanonicaliseTerm(value);

        var pooledTerm = Buffer.GetOrCreateQualifiedTerm(fieldName, term.AsSpan());

        var acc = Buffer.GetOrCreateAccumulator(pooledTerm);
        long before = acc.EstimatedBytes;
        acc.AddDocOnly(docId);
        Buffer.PostingsRamBytes += acc.EstimatedBytes - before;

        if ((docValues & StringDocValues.Sorted) != 0)
        {
            if (!Buffer.SortedDocValues.TryGetValue(fieldName, out var dvList))
            {
                dvList = new List<string?>();
                Buffer.SortedDocValues[fieldName] = dvList;
            }
            while (dvList.Count <= docId) dvList.Add(null);
            dvList[docId] = value;

        }

        if ((docValues & StringDocValues.SortedSet) != 0)
            AddSortedSetDocValue(fieldName, docId, value);
    }

    private void IndexNumericField(string fieldName, double value, int docId, bool storeDocValues = true)
    {
        if (!Buffer.NumericIndex.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, double>();
            Buffer.NumericIndex[fieldName] = fieldMap;
        }
        fieldMap[docId] = value;

        if (storeDocValues)
        {
            if (!Buffer.NumericDocValues.TryGetValue(fieldName, out var dvList))
            {
                dvList = new List<double>();
                Buffer.NumericDocValues[fieldName] = dvList;
            }
            while (dvList.Count <= docId)
                dvList.Add(0);
            dvList[docId] = value;

            AddSortedNumericDocValue(fieldName, docId, value);
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendStoredField(string fieldName, StoredFieldValue value, bool mirrorStringToBinaryDocValues = true, bool storeDocValues = true)
    {
        if (!Buffer.StoredFieldNameToId.TryGetValue(fieldName, out int fid))
        {
            fid = Buffer.StoredFieldIdToName.Count;
            Buffer.StoredFieldNameToId[fieldName] = fid;
            Buffer.StoredFieldIdToName.Add(fieldName);
        }
        Buffer.StoredFieldIds.Add(fid);
        Buffer.StoredFieldValues.Add(value);
        if (storeDocValues)
        {
            if (value.IsBinary)
            {
                AddBinaryDocValue(fieldName, Buffer.DocCount, value.BinaryValue ?? []);
            }
            else if (mirrorStringToBinaryDocValues && value.StringValue is not null)
            {
                AddBinaryDocValue(fieldName, Buffer.DocCount, value.StringValue);
            }
        }
    }

    private void AddSortedSetDocValue(string fieldName, int docId, string value)
    {
        if (!Buffer.SortedSetDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<string>>();
            Buffer.SortedSetDocValues[fieldName] = fieldMap;
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
        if (!Buffer.SortedNumericDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<double>>();
            Buffer.SortedNumericDocValues[fieldName] = fieldMap;
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
        if (!Buffer.Int64Index.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, long>();
            Buffer.Int64Index[fieldName] = fieldMap;
        }
        fieldMap[docId] = value;

        if (storeDocValues)
        {
            if (!Buffer.Int64DocValues.TryGetValue(fieldName, out var dvList))
            {
                dvList = new List<long>();
                Buffer.Int64DocValues[fieldName] = dvList;
            }
            while (dvList.Count <= docId)
                dvList.Add(0);
            dvList[docId] = value;

            AddSortedInt64DocValue(fieldName, docId, value);
        }
    }

    private void AddSortedInt64DocValue(string fieldName, int docId, long value)
    {
        if (!Buffer.Int64SortedDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<long>>();
            Buffer.Int64SortedDocValues[fieldName] = fieldMap;
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
        if (!Buffer.BinaryDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<byte[]>>();
            Buffer.BinaryDocValues[fieldName] = fieldMap;
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

        if (!Buffer.FieldBoosts.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, float>();
            Buffer.FieldBoosts[fieldName] = fieldMap;
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



    private sealed class SpanCountingTokenSink : ISpanTokenSink
    {
        private int _limit;

        public int Count { get; private set; }

        public bool ExceededLimit => _limit > 0 && Count > _limit;

        public void Reset(int limit)
        {
            _limit = limit;
            Count = 0;
        }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            Count++;
        }
    }

    private sealed class SpanPostingTokenSink : ISpanTokenSink
    {
        private readonly DocumentBufferState _buffer;
        private readonly IndexWriterConfig _config;
        private string _fieldName = string.Empty;
        private int _docId;
        private int _budget;
        private Analysis.TokenBudgetPolicy _budgetPolicy;
        private int _position;
        private FieldIndexOptions _fieldIndexOptions;

        public SpanPostingTokenSink(DocumentBufferState buffer, IndexWriterConfig config)
        {
            _buffer = buffer;
            _config = config;
        }

        public int AcceptedCount { get; private set; }

        public void Reset(string fieldName, int docId, int budget, Analysis.TokenBudgetPolicy budgetPolicy,
            FieldIndexOptions indexOptions)
        {
            _fieldName = fieldName;
            _docId = docId;
            _budget = budget;
            _budgetPolicy = budgetPolicy;
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
            if (_budget > 0 &&
                _budgetPolicy == Analysis.TokenBudgetPolicy.Truncate &&
                AcceptedCount >= _budget)
            {
                return;
            }

            int increment = positionIncrement > 0 ? positionIncrement : 0;
            if (_position < 0 && increment == 0)
                increment = 1;
            _position += increment;

            _buffer.AccumulatePosting(_fieldName, text, _docId, _position, payload, _config.StorePayloads,
                _fieldIndexOptions, startOffset, endOffset);
            AcceptedCount++;
        }
    }
}
