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
        int localDocId = _buffer.DocCount;
        _buffer.StoredDocStarts.Add(_buffer.StoredFieldIds.Count);
        Dictionary<string, double>? numericDoc = null;
        int storedEntryStart = _buffer.StoredFieldIds.Count;

        foreach (var field in doc.Fields)
        {
            switch (field)
            {
                case TextField tf:
                    TrackFieldBoost(tf.Name, localDocId, tf.Boost);
                    IndexTextField(tf.Name, tf.Value, localDocId);
                    if (tf.IsStored)
                    {
                        AppendStoredField(tf.Name, StoredFieldValue.FromString(tf.Value), mirrorStringToBinaryDocValues: false);
                    }
                    break;
                case StringField sf:
                    TrackFieldBoost(sf.Name, localDocId, sf.Boost);
                    IndexStringField(sf.Name, sf.Value, localDocId);
                    if (sf.IsStored)
                    {
                        AppendStoredField(sf.Name, StoredFieldValue.FromString(sf.Value));
                    }
                    break;
                case NumericField nf:
                    TrackFieldBoost(nf.Name, localDocId, nf.Boost);
                    IndexNumericField(nf.Name, nf.Value, localDocId);
                    numericDoc ??= new Dictionary<string, double>();
                    if (nf.IsStored)
                    {
                        AppendStoredField(nf.Name, StoredFieldValue.FromString(nf.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    }
                    break;
                case VectorField vf:
                    TrackFieldBoost(vf.Name, localDocId, vf.Boost);
                    if (!_buffer.Vectors.TryGetValue(vf.Name, out var perField))
                    {
                        perField = new Dictionary<int, ReadOnlyMemory<float>>();
                        _buffer.Vectors[vf.Name] = perField;
                    }
                    perField[localDocId] = vf.Value;
                    break;
                case GeoPointField gf:
                    TrackFieldBoost(gf.Name, localDocId, gf.Boost);
                    IndexNumericField(gf.LatFieldName, gf.Latitude, localDocId);
                    IndexNumericField(gf.LonFieldName, gf.Longitude, localDocId);
                    if (gf.IsStored)
                        AppendStoredField(gf.Name, StoredFieldValue.FromString(gf.Value));
                    break;
                case StoredField sf:
                    AppendStoredField(sf.Name, StoredFieldValue.FromString(sf.Value));
                    break;
                case BinaryField bf:
                    AppendStoredField(bf.Name, StoredFieldValue.FromBinary(bf.Value.Span));
                    break;
            }
        }

        if (numericDoc is not null)
            _buffer.NumericFields.Add(numericDoc);

        if (_config.TrackSequenceNumbers)
        {
            if (_buffer.DocCount == 0 && localDocId == 0)
                _flushSeqNoStart = _nextSequenceNumber;
            _nextSequenceNumber++;
        }

        _buffer.DocCount++;
        _contentChangedSinceCommit = true;

        // Track stored-field RAM (postings tracked accurately via EstimatedBytes)
        for (int i = storedEntryStart; i < _buffer.StoredFieldIds.Count; i++)
            _buffer.EstimatedRamBytes += _buffer.StoredFieldValues[i].EstimatedSize;

        // Check flush thresholds
        if (!suppressFlush && ShouldFlush())
            FlushSegment();
    }

    private void IndexTextField(string fieldName, string value, int docId)
    {
        // Apply char filters before tokenisation
        ReadOnlySpan<char> input = value.AsSpan();
        string? filtered = null;
        if (_config.CharFilters.Count > 0)
        {
            filtered = value;
            foreach (var cf in _config.CharFilters)
                filtered = cf.Filter(filtered.AsSpan());
            input = filtered.AsSpan();
        }

        if (!_analyserCache.TryGetValue(fieldName, out var analyser))
        {
            analyser = _config.FieldAnalysers.GetValueOrDefault(fieldName, _defaultAnalyser);
            _analyserCache[fieldName] = analyser;
        }

        if (TryIndexTextFieldWithSpanAnalyser(analyser, input, fieldName, docId))
            return;

        var tokens = analyser.Analyse(input);

        // Enforce token budget if configured
        int budget = _config.MaxTokensPerDocument;
        if (budget > 0 && tokens.Count > budget)
        {
            switch (_config.TokenBudgetPolicy)
            {
                case Analysis.TokenBudgetPolicy.Truncate:
                    tokens.RemoveRange(budget, tokens.Count - budget);
                    break;
                case Analysis.TokenBudgetPolicy.Warn:
                    // Continue with all tokens; caller can observe via metrics
                    break;
                case Analysis.TokenBudgetPolicy.Reject:
                    throw new Analysis.TokenBudgetExceededException(tokens.Count, budget);
            }
        }

        AddTokenCount(fieldName, docId, tokens.Count);

        _buffer.FieldNames.Add(fieldName);

        int pos = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            int increment = tokens[i].PositionIncrement > 0 ? tokens[i].PositionIncrement : 0;
            if (pos < 0 && increment == 0)
                increment = 1;
            pos += increment;

            var term = _buffer.CanonicaliseTerm(tokens[i].Text);
            _buffer.AccumulatePosting(fieldName, term.AsSpan(), docId, pos, tokens[i].Payload, _config.StorePayloads);
        }
    }

    private bool TryIndexTextFieldWithSpanAnalyser(IAnalyser analyser, ReadOnlySpan<char> input, string fieldName, int docId)
    {
        if (analyser is not ISpanAnalyser spanAnalyser)
            return false;

        int budget = _config.MaxTokensPerDocument;
        if (budget > 0 && _config.TokenBudgetPolicy == Analysis.TokenBudgetPolicy.Reject)
        {
            _spanCountingSink.Reset(limit: budget);
            if (!spanAnalyser.TryAnalyse(input, _spanCountingSink))
                return false;
            if (_spanCountingSink.ExceededLimit)
                throw new Analysis.TokenBudgetExceededException(_spanCountingSink.Count, budget);
        }

        _spanPostingSink.Reset(fieldName, docId, budget, _config.TokenBudgetPolicy);
        if (!spanAnalyser.TryAnalyse(input, _spanPostingSink))
            return false;

        AddTokenCount(fieldName, docId, _spanPostingSink.AcceptedCount);
        _buffer.FieldNames.Add(fieldName);
        return true;
    }

    private void AddTokenCount(string fieldName, int docId, int tokenCount)
    {
        if (!_buffer.DocTokenCounts.TryGetValue(fieldName, out var counts))
        {
            counts = new int[_config.MaxBufferedDocs];
            _buffer.DocTokenCounts[fieldName] = counts;
        }
        else if (docId >= counts.Length)
        {
            // Rare case: exceeded MaxBufferedDocs, grow the array
            Array.Resize(ref counts, Math.Max(counts.Length * 2, docId + 1));
            _buffer.DocTokenCounts[fieldName] = counts;
        }
        counts[docId] += tokenCount;
    }

    private void IndexStringField(string fieldName, string value, int docId)
    {
        _buffer.FieldNames.Add(fieldName);
        var term = _buffer.CanonicaliseTerm(value);

        var pooledTerm = _buffer.GetOrCreateQualifiedTerm(fieldName, term.AsSpan());

        var acc = _buffer.GetOrCreateAccumulator(pooledTerm);
        long before = acc.EstimatedBytes;
        acc.AddDocOnly(docId);
        _buffer.PostingsRamBytes += acc.EstimatedBytes - before;

        // Also populate SortedDocValues for collapsing/faceting
        if (!_buffer.SortedDocValues.TryGetValue(fieldName, out var dvList))
        {
            dvList = new List<string?>();
            _buffer.SortedDocValues[fieldName] = dvList;
        }
        while (dvList.Count <= docId) dvList.Add(null);
        dvList[docId] = value;

        AddSortedSetDocValue(fieldName, docId, value);
    }

    private void IndexNumericField(string fieldName, double value, int docId)
    {
        if (!_buffer.NumericIndex.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, double>();
            _buffer.NumericIndex[fieldName] = fieldMap;
        }
        fieldMap[docId] = value;

        // Also accumulate for NumericDocValues column-stride storage
        if (!_buffer.NumericDocValues.TryGetValue(fieldName, out var dvList))
        {
            dvList = new List<double>();
            _buffer.NumericDocValues[fieldName] = dvList;
        }
        // Pad with 0 for any skipped docs.
        while (dvList.Count <= docId)
            dvList.Add(0);
        dvList[docId] = value;

        AddSortedNumericDocValue(fieldName, docId, value);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendStoredField(string fieldName, StoredFieldValue value, bool mirrorStringToBinaryDocValues = true)
    {
        if (!_buffer.StoredFieldNameToId.TryGetValue(fieldName, out int fid))
        {
            fid = _buffer.StoredFieldIdToName.Count;
            _buffer.StoredFieldNameToId[fieldName] = fid;
            _buffer.StoredFieldIdToName.Add(fieldName);
        }
        _buffer.StoredFieldIds.Add(fid);
        _buffer.StoredFieldValues.Add(value);
        if (value.IsBinary)
        {
            AddBinaryDocValue(fieldName, _buffer.DocCount, value.BinaryValue ?? []);
        }
        else if (mirrorStringToBinaryDocValues && value.StringValue is not null)
        {
            AddBinaryDocValue(fieldName, _buffer.DocCount, value.StringValue);
        }
    }

    private void AddSortedSetDocValue(string fieldName, int docId, string value)
    {
        if (!_buffer.SortedSetDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<string>>();
            _buffer.SortedSetDocValues[fieldName] = fieldMap;
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
        if (!_buffer.SortedNumericDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<double>>();
            _buffer.SortedNumericDocValues[fieldName] = fieldMap;
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
        AddBinaryDocValue(fieldName, docId, System.Text.Encoding.UTF8.GetBytes(value));
    }

    private void AddBinaryDocValue(string fieldName, int docId, ReadOnlySpan<byte> value)
    {
        if (!_buffer.BinaryDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<byte[]>>();
            _buffer.BinaryDocValues[fieldName] = fieldMap;
        }

        if (!fieldMap.TryGetValue(docId, out var values))
        {
            values = [];
            fieldMap[docId] = values;
        }

        values.Add(value.ToArray());
    }

    private void TrackFieldBoost(string fieldName, int docId, float boost)
    {
        if (boost == 1.0f)
            return;

        if (!_buffer.FieldBoosts.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, float>();
            _buffer.FieldBoosts[fieldName] = fieldMap;
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

        public SpanPostingTokenSink(DocumentBufferState buffer, IndexWriterConfig config)
        {
            _buffer = buffer;
            _config = config;
        }

        public int AcceptedCount { get; private set; }

        public void Reset(string fieldName, int docId, int budget, Analysis.TokenBudgetPolicy budgetPolicy)
        {
            _fieldName = fieldName;
            _docId = docId;
            _budget = budget;
            _budgetPolicy = budgetPolicy;
            _position = -1;
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

            _buffer.AccumulatePosting(_fieldName, text, _docId, _position, payload, _config.StorePayloads);
            AcceptedCount++;
        }
    }
}
