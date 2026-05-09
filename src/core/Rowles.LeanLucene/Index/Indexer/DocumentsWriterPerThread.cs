using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Document;

namespace Rowles.LeanLucene.Index.Indexer;

/// <summary>
/// A per-thread document buffer for concurrent indexing.
/// Each thread accumulates postings independently; the writer merges them on flush.
/// </summary>
internal sealed class DocumentsWriterPerThread
{
    private readonly IAnalyser _analyser;
    private readonly Dictionary<string, IAnalyser> _fieldAnalysers;
    internal readonly Dictionary<string, PostingAccumulator> Postings = new(StringComparer.Ordinal);
    internal readonly List<Dictionary<string, List<string>>> StoredFields = [];
    internal readonly Dictionary<string, Dictionary<int, double>> NumericIndex = new();
    internal readonly Dictionary<string, List<double>> NumericDocValues = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, List<string?>> SortedDocValues = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Dictionary<int, List<string>>> SortedSetDocValues = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Dictionary<int, List<double>>> SortedNumericDocValues = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Dictionary<int, List<byte[]>>> BinaryDocValues = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>> Vectors = new(StringComparer.Ordinal);
    internal readonly HashSet<string> FieldNames = new(StringComparer.Ordinal);
    // Per-field token counts: field → docId → count
    internal Dictionary<string, int[]> DocTokenCounts = new(StringComparer.Ordinal);
    internal int DocCount;
    private readonly Dictionary<(string, string), string> _qualifiedTermPool = new();
    private readonly HashSet<string> _termPool = new(StringComparer.Ordinal);
    private long _estimatedRamBytes;

    /// <summary>Estimated RAM usage in bytes for this DWPT's buffers.</summary>
    public long EstimatedRamBytes => Volatile.Read(ref _estimatedRamBytes);

    public DocumentsWriterPerThread(IAnalyser defaultAnalyser, Dictionary<string, IAnalyser> fieldAnalysers)
    {
        _analyser = defaultAnalyser;
        _fieldAnalysers = fieldAnalysers;
    }

    /// <summary>
    /// Indexes a single document into this thread's local buffer.
    /// Not thread-safe — each thread owns its own DWPT instance.
    /// </summary>
    public void AddDocument(LeanDocument doc)
    {
        int localDocId = DocCount;
        var storedDoc = new Dictionary<string, List<string>>(8);

        foreach (var field in doc.Fields)
        {
            switch (field)
            {
                case TextField tf:
                    IndexTextField(tf.Name, tf.Value, localDocId);
                    if (tf.IsStored)
                    {
                        AddStored(storedDoc, tf.Name, tf.Value);
                        AddBinaryDocValue(tf.Name, localDocId, tf.Value);
                        _estimatedRamBytes += tf.Value.Length * 2 + 64;
                    }
                    break;
                case StringField sf:
                    IndexStringField(sf.Name, sf.Value, localDocId);
                    if (sf.IsStored)
                    {
                        AddStored(storedDoc, sf.Name, sf.Value);
                        AddBinaryDocValue(sf.Name, localDocId, sf.Value);
                        _estimatedRamBytes += sf.Value.Length * 2 + 64;
                    }
                    break;
                case NumericField nf:
                    IndexNumericField(nf.Name, nf.Value, localDocId);
                    if (nf.IsStored)
                    {
                        var storedValue = nf.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        AddStored(storedDoc, nf.Name, storedValue);
                        AddBinaryDocValue(nf.Name, localDocId, storedValue);
                        _estimatedRamBytes += 48;
                    }
                    break;
                case StoredField sf:
                    AddStored(storedDoc, sf.Name, sf.Value);
                    AddBinaryDocValue(sf.Name, localDocId, sf.Value);
                    _estimatedRamBytes += sf.Value.Length * 2 + 64;
                    break;
                case VectorField vf:
                    IndexVectorField(vf.Name, vf.Value, localDocId);
                    break;
                case GeoPointField gf:
                    IndexNumericField(gf.LatFieldName, gf.Latitude, localDocId);
                    IndexNumericField(gf.LonFieldName, gf.Longitude, localDocId);
                    if (gf.IsStored)
                    {
                        AddStored(storedDoc, gf.Name, gf.Value);
                        AddBinaryDocValue(gf.Name, localDocId, gf.Value);
                        _estimatedRamBytes += gf.Value.Length * 2 + 64;
                    }
                    break;
            }
        }

        StoredFields.Add(storedDoc);
        DocCount++;
        _estimatedRamBytes += 128; // Dictionary + List overhead per doc
    }

    private static void AddStored(Dictionary<string, List<string>> storedDoc, string name, string value)
    {
        if (!storedDoc.TryGetValue(name, out var list))
        {
            list = new List<string>();
            storedDoc[name] = list;
        }
        list.Add(value);
    }

    private void IndexTextField(string fieldName, string value, int docId)
    {
        var analyser = _fieldAnalysers.GetValueOrDefault(fieldName, _analyser);
        var tokens = analyser.Analyse(value.AsSpan());

        // Track per-field token counts
        if (!DocTokenCounts.TryGetValue(fieldName, out var counts))
        {
            counts = new int[16];
            DocTokenCounts[fieldName] = counts;
        }
        if (docId >= counts.Length)
            Array.Resize(ref counts, Math.Max(counts.Length * 2, docId + 1));
        counts[docId] += tokens.Count;
        DocTokenCounts[fieldName] = counts; // Update reference in case of resize

        FieldNames.Add(fieldName);

        for (int pos = 0; pos < tokens.Count; pos++)
        {
            var term = CanonicaliseTerm(tokens[pos].Text);
            var qualifiedTerm = GetQualifiedTerm(fieldName, term);

            if (!Postings.TryGetValue(qualifiedTerm, out var acc))
            {
                acc = new PostingAccumulator();
                Postings[qualifiedTerm] = acc;
                _estimatedRamBytes += qualifiedTerm.Length * 2 + 128; // new term + accumulator
            }
            acc.Add(docId, pos);
            _estimatedRamBytes += 12; // posting entry (docId + position)
        }
    }

    private void IndexStringField(string fieldName, string value, int docId)
    {
        FieldNames.Add(fieldName);
        var term = CanonicaliseTerm(value);
        var qualifiedTerm = GetQualifiedTerm(fieldName, term);

        if (!Postings.TryGetValue(qualifiedTerm, out var acc))
        {
            acc = new PostingAccumulator();
            Postings[qualifiedTerm] = acc;
        }
        acc.AddDocOnly(docId);

        // Populate sorted DV column so collapse/facet behave the same as on the main writer.
        if (!SortedDocValues.TryGetValue(fieldName, out var dvList))
        {
            dvList = new List<string?>();
            SortedDocValues[fieldName] = dvList;
        }
        while (dvList.Count <= docId) dvList.Add(null);
        dvList[docId] = value;
        AddSortedSetDocValue(fieldName, docId, value);
        _estimatedRamBytes += value.Length * 2 + 16;
    }

    private void IndexNumericField(string fieldName, double value, int docId)
    {
        FieldNames.Add(fieldName);
        if (!NumericIndex.TryGetValue(fieldName, out var fieldMap))
        {
            fieldMap = new Dictionary<int, double>();
            NumericIndex[fieldName] = fieldMap;
        }
        fieldMap[docId] = value;

        if (!NumericDocValues.TryGetValue(fieldName, out var dvList))
        {
            dvList = new List<double>();
            NumericDocValues[fieldName] = dvList;
        }
        while (dvList.Count <= docId) dvList.Add(0);
        dvList[docId] = value;
        AddSortedNumericDocValue(fieldName, docId, value);
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

    private void AddBinaryDocValue(string fieldName, int docId, string value)
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

        values.Add(System.Text.Encoding.UTF8.GetBytes(value));
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

    private string GetQualifiedTerm(string fieldName, string term)
    {
        if (!_qualifiedTermPool.TryGetValue((fieldName, term), out var qt))
        {
            qt = string.Concat(fieldName, "\x00", term);
            _qualifiedTermPool[(fieldName, term)] = qt;
        }
        return qt;
    }
}
