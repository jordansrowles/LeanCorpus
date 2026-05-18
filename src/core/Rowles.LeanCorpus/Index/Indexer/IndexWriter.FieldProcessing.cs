using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    private void AddDocumentCore(LeanDocument doc, bool suppressFlush = false)
    {
        int localDocId = _bufferedDocCount;
        _sfDocStarts.Add(_sfFieldIds.Count);
        Dictionary<string, double>? numericDoc = null;
        int storedEntryStart = _sfFieldIds.Count;

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
                    if (!_bufferedVectors.TryGetValue(vf.Name, out var perField))
                    {
                        perField = new Dictionary<int, ReadOnlyMemory<float>>();
                        _bufferedVectors[vf.Name] = perField;
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
            _numericFields.Add(numericDoc);
        _bufferedDocCount++;
        _contentChangedSinceCommit = true;

        // Track stored-field RAM (postings tracked accurately via EstimatedBytes)
        for (int i = storedEntryStart; i < _sfFieldIds.Count; i++)
            _estimatedRamBytes += _sfValues[i].EstimatedSize;

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

        // Track per-field token count for O(1) per-field norm computation
        // Pre-allocate to MaxBufferedDocs to avoid resize overhead during indexing
        if (!_docTokenCounts.TryGetValue(fieldName, out var counts))
        {
            counts = new int[_config.MaxBufferedDocs];
            _docTokenCounts[fieldName] = counts;
        }
        else if (docId >= counts.Length)
        {
            // Rare case: exceeded MaxBufferedDocs, grow the array
            Array.Resize(ref counts, Math.Max(counts.Length * 2, docId + 1));
            _docTokenCounts[fieldName] = counts;
        }
        counts[docId] += tokens.Count;

        _fieldNames.Add(fieldName);

        int pos = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            int increment = tokens[i].PositionIncrement > 0 ? tokens[i].PositionIncrement : 0;
            if (pos < 0 && increment == 0)
                increment = 1;
            pos += increment;

            var term = CanonicaliseTerm(tokens[i].Text);

            var pooledTerm = GetOrCreateQualifiedTerm(fieldName, term);

            if (!_postings.TryGetValue(pooledTerm, out var acc))
            {
                acc = new PostingAccumulator();
                _postings[pooledTerm] = acc;
                _postingsRamBytes += acc.EstimatedBytes;
            }
            long before = acc.EstimatedBytes;
            var payload = tokens[i].Payload;
            if (_config.StorePayloads && (acc.HasPayloads || payload is { Length: > 0 }))
            {
                acc.AddWithPayload(docId, pos, payload);
            }
            else
            {
                acc.Add(docId, pos);
            }
            _postingsRamBytes += acc.EstimatedBytes - before;
        }
    }

    private void IndexStringField(string fieldName, string value, int docId)
    {
        _fieldNames.Add(fieldName);
        var term = CanonicaliseTerm(value);

        var pooledTerm = GetOrCreateQualifiedTerm(fieldName, term);

        if (!_postings.TryGetValue(pooledTerm, out var acc))
        {
            acc = new PostingAccumulator();
            _postings[pooledTerm] = acc;
            _postingsRamBytes += acc.EstimatedBytes;
        }
        long before = acc.EstimatedBytes;
        acc.AddDocOnly(docId);
        _postingsRamBytes += acc.EstimatedBytes - before;

        // Also populate SortedDocValues for collapsing/faceting
        if (!_sortedDocValues.TryGetValue(fieldName, out var dvList))
        {
            dvList = new List<string?>();
            _sortedDocValues[fieldName] = dvList;
        }
        while (dvList.Count <= docId) dvList.Add(null);
        dvList[docId] = value;

        AddSortedSetDocValue(fieldName, docId, value);
    }

    private void IndexNumericField(string fieldName, double value, int docId)
    {
        if (!_numericIndex.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, double>();
            _numericIndex[fieldName] = fieldMap;
        }
        fieldMap[docId] = value;

        // Also accumulate for NumericDocValues column-stride storage
        if (!_numericDocValues.TryGetValue(fieldName, out var dvList))
        {
            dvList = new List<double>();
            _numericDocValues[fieldName] = dvList;
        }
        // Pad with 0 for any skipped docs.
        while (dvList.Count <= docId)
            dvList.Add(0);
        dvList[docId] = value;

        AddSortedNumericDocValue(fieldName, docId, value);
    }

    private void WriteNumericIndex(string filePath)
    {
        if (_numericIndex.Count == 0) return;

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        writer.Write(_numericIndex.Count); // field count
        foreach (var (fieldName, docValues) in _numericIndex)
        {
            writer.Write(fieldName);
            writer.Write(docValues.Count);
            foreach (var (docId, value) in docValues)
            {
                writer.Write(docId);
                writer.Write(value);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendStoredField(string fieldName, StoredFieldValue value, bool mirrorStringToBinaryDocValues = true)
    {
        if (!_sfFieldNameToId.TryGetValue(fieldName, out int fid))
        {
            fid = _sfFieldIdToName.Count;
            _sfFieldNameToId[fieldName] = fid;
            _sfFieldIdToName.Add(fieldName);
        }
        _sfFieldIds.Add(fid);
        _sfValues.Add(value);
        if (value.IsBinary)
        {
            AddBinaryDocValue(fieldName, _bufferedDocCount, value.BinaryValue ?? []);
        }
        else if (mirrorStringToBinaryDocValues && value.StringValue is not null)
        {
            AddBinaryDocValue(fieldName, _bufferedDocCount, value.StringValue);
        }
    }

    private void AddSortedSetDocValue(string fieldName, int docId, string value)
    {
        if (!_sortedSetDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<string>>();
            _sortedSetDocValues[fieldName] = fieldMap;
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
        if (!_sortedNumericDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<double>>();
            _sortedNumericDocValues[fieldName] = fieldMap;
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
        if (!_binaryDocValues.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, List<byte[]>>();
            _binaryDocValues[fieldName] = fieldMap;
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

        if (!_fieldBoosts.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, float>();
            _fieldBoosts[fieldName] = fieldMap;
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

    private string CanonicaliseTerm(string term)
    {
        if (_termPool.TryGetValue(term, out var canonical))
            return canonical;

        _termPool.Add(term);
        return term;
    }

    /// <summary>
    /// Returns a pooled qualified term string ("field\0term"). Uses span-based alternate
    /// lookup to avoid allocating a string on cache hit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetOrCreateQualifiedTerm(string fieldName, string term)
    {
        if (!_fieldPrefixCache.TryGetValue(fieldName, out var prefix))
        {
            prefix = string.Concat(fieldName, "\x00");
            _fieldPrefixCache[fieldName] = prefix;
        }

        // Build the qualified term into a stack buffer to probe the pool without allocating
        int totalLen = prefix.Length + term.Length;
        Span<char> buf = totalLen <= 256 ? stackalloc char[totalLen] : new char[totalLen];
        prefix.AsSpan().CopyTo(buf);
        term.AsSpan().CopyTo(buf[prefix.Length..]);

        var lookup = _qualifiedTermPool.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(buf, out var pooled))
            return pooled;

        var qualifiedTerm = new string(buf);
        _qualifiedTermPool[qualifiedTerm] = qualifiedTerm;
        return qualifiedTerm;
    }
}
