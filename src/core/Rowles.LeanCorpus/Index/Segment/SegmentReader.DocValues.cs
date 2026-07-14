using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// DocValues and numeric index-related methods for SegmentReader.
/// </summary>
public sealed partial class SegmentReader
{
    /// <summary>Lazy-loads the numeric index (.num) for range queries.</summary>
    private Dictionary<string, Dictionary<int, double>> EnsureNumericIndex()
    {
        return LazyInitializer.EnsureInitialized(ref _numericIndex, ref _lazyInitLock, () =>
        {
            var numPath = _basePath + ".num";
            return File.Exists(numPath)
                ? ReadNumericIndex(numPath)
                : new Dictionary<string, Dictionary<int, double>>();
        })!;
    }

    /// <summary>Lazy-loads numeric doc values (.dvn) and their presence bitmaps.</summary>
    private Dictionary<string, double[]> EnsureNumericDocValues()
    {
        if (_numericDocValues is not null) return _numericDocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_numericDocValues is not null) return _numericDocValues;
            var (vals, pres) = NumericDocValuesReader.Read(_basePath + ".dvn");
            _numericDocValuesPresence = pres;
            _numericDocValues = vals;
        }
        return _numericDocValues;
    }

    /// <summary>Lazy-loads the 64-bit integer index (.numl) for range queries.</summary>
    private Dictionary<string, Dictionary<int, long>> EnsureInt64Index()
    {
        return LazyInitializer.EnsureInitialized(ref _int64Index, ref _lazyInitLock, () =>
        {
            var numlPath = _basePath + ".numl";
            return File.Exists(numlPath)
                ? ReadInt64Index(numlPath)
                : new Dictionary<string, Dictionary<int, long>>();
        })!;
    }

    /// <summary>Lazy-loads 64-bit integer doc values (.dvnl) and their presence bitmaps.</summary>
    private Dictionary<string, long[]> EnsureInt64DocValues()
    {
        if (_int64DocValues is not null) return _int64DocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_int64DocValues is not null) return _int64DocValues;
            var (vals, pres) = Int64DocValuesReader.Read(_basePath + ".dvnl");
            _int64DocValuesPresence = pres;
            _int64DocValues = vals;
        }
        return _int64DocValues;
    }

    /// <summary>Lazy-loads 64-bit integer sorted-numeric doc values (.dsnl).</summary>
    private Dictionary<string, long[][]> EnsureInt64SortedDocValues()
    {
        if (_int64SortedDocValues is not null) return _int64SortedDocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_int64SortedDocValues is not null) return _int64SortedDocValues;
            _int64SortedDocValues = Int64SortedNumericDocValuesReader.Read(_basePath + ".dsnl");
        }
        return _int64SortedDocValues;
    }

    /// <summary>Lazy-loads sorted doc values (.dvs) and their presence bitmaps.</summary>
    private Dictionary<string, string[]> EnsureSortedDocValues()
    {
        if (_sortedDocValues is not null) return _sortedDocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_sortedDocValues is not null) return _sortedDocValues;
            var (vals, pres) = SortedDocValuesReader.Read(_basePath + ".dvs");
            _sortedDocValuesPresence = pres;
            _sortedDocValues = vals;
        }
        return _sortedDocValues;
    }

    /// <summary>Lazy-loads sorted-set doc values (.dss).</summary>
    private Dictionary<string, string[][]> EnsureSortedSetDocValues()
    {
        if (_sortedSetDocValues is not null) return _sortedSetDocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_sortedSetDocValues is not null) return _sortedSetDocValues;
            _sortedSetDocValues = SortedSetDocValuesReader.Read(_basePath + ".dss");
        }
        return _sortedSetDocValues;
    }

    /// <summary>Lazy-loads sorted-numeric doc values (.dsn).</summary>
    private Dictionary<string, double[][]> EnsureSortedNumericDocValues()
    {
        if (_sortedNumericDocValues is not null) return _sortedNumericDocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_sortedNumericDocValues is not null) return _sortedNumericDocValues;
            _sortedNumericDocValues = SortedNumericDocValuesReader.Read(_basePath + ".dsn");
        }
        return _sortedNumericDocValues;
    }

    /// <summary>Lazy-loads binary doc values (.dvb).</summary>
    private Dictionary<string, byte[][][]> EnsureBinaryDocValues()
    {
        if (_binaryDocValues is not null) return _binaryDocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_binaryDocValues is not null) return _binaryDocValues;
            _binaryDocValues = BinaryDocValuesReader.Read(_basePath + ".dvb");
        }
        return _binaryDocValues;
    }

    /// <summary>
    /// Tries to get a numeric field value for a document from the .num index.
    /// </summary>
    public bool TryGetNumericValue(string field, int docId, out double value)
    {
        value = 0;
        var numericIndex = EnsureNumericIndex();
        if (numericIndex.TryGetValue(field, out var fieldMap))
            return fieldMap.TryGetValue(docId, out value);

        // Legacy fallback for segments that predate the sparse .num index.
        var numericDocValues = EnsureNumericDocValues();
        if (numericDocValues.TryGetValue(field, out var dvArr) && (uint)docId < (uint)dvArr.Length)
        {
            // Use the presence bitmap (v2 files) to distinguish truly absent docs from
            // docs that have an explicit zero value.
            if (_numericDocValuesPresence is not null &&
                _numericDocValuesPresence.TryGetValue(field, out var presenceBitmap) &&
                presenceBitmap is not null &&
                !presenceBitmap.Contains(docId))
            {
                return false;
            }

            value = dvArr[docId];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to get a 64-bit integer field value for a document from the .numl index.
    /// </summary>
    public bool TryGetInt64Value(string field, int docId, out long value)
    {
        value = 0;
        var int64Index = EnsureInt64Index();
        if (int64Index.TryGetValue(field, out var fieldMap))
            return fieldMap.TryGetValue(docId, out value);

        var int64DocValues = EnsureInt64DocValues();
        if (int64DocValues.TryGetValue(field, out var dvArr) && (uint)docId < (uint)dvArr.Length)
        {
            if (_int64DocValuesPresence is not null &&
                _int64DocValuesPresence.TryGetValue(field, out var presenceBitmap) &&
                presenceBitmap is not null &&
                !presenceBitmap.Contains(docId))
            {
                return false;
            }

            value = dvArr[docId];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to get a string DocValues field value for a document.
    /// </summary>
    public bool TryGetSortedDocValue(string field, int docId, out string value)
    {
        value = string.Empty;
        var sortedDocValues = EnsureSortedDocValues();
        if (sortedDocValues.TryGetValue(field, out var arr) && (uint)docId < (uint)arr.Length)
        {
            // Use the presence bitmap (v2 files) to distinguish absent docs from those
            // with an explicitly empty string value.
            if (_sortedDocValuesPresence is not null &&
                _sortedDocValuesPresence.TryGetValue(field, out var presenceBitmap) &&
                presenceBitmap is not null &&
                !presenceBitmap.Contains(docId))
            {
                return false;
            }

            value = arr[docId];
            return true;
        }
        return false;
    }

    /// <summary>
    /// Tries to get sorted-set DocValues for a document.
    /// </summary>
    public bool TryGetSortedSetDocValues(string field, int docId, out IReadOnlyList<string> values)
    {
        values = [];
        var docValues = EnsureSortedSetDocValues();
        if (!docValues.TryGetValue(field, out var arr) || (uint)docId >= (uint)arr.Length || arr[docId].Length == 0)
            return false;

        values = arr[docId];
        return true;
    }

    /// <summary>
    /// Tries to get sorted-numeric DocValues for a document.
    /// </summary>
    public bool TryGetSortedNumericDocValues(string field, int docId, out IReadOnlyList<double> values)
    {
        values = [];
        var docValues = EnsureSortedNumericDocValues();
        if (!docValues.TryGetValue(field, out var arr) || (uint)docId >= (uint)arr.Length || arr[docId].Length == 0)
            return false;

        values = arr[docId];
        return true;
    }

    /// <summary>
    /// Tries to get 64-bit integer sorted-numeric DocValues for a document.
    /// </summary>
    public bool TryGetSortedInt64DocValues(string field, int docId, out IReadOnlyList<long> values)
    {
        values = [];
        var docValues = EnsureInt64SortedDocValues();
        if (!docValues.TryGetValue(field, out var arr) || (uint)docId >= (uint)arr.Length || arr[docId].Length == 0)
            return false;

        values = arr[docId];
        return true;
    }

    /// <summary>
    /// Tries to get binary DocValues for a document.
    /// </summary>
    public bool TryGetBinaryDocValues(string field, int docId, out IReadOnlyList<byte[]> values)
    {
        values = [];
        var docValues = EnsureBinaryDocValues();
        if (!docValues.TryGetValue(field, out var arr) || (uint)docId >= (uint)arr.Length || arr[docId].Length == 0)
            return false;

        values = arr[docId];
        return true;
    }

    /// <summary>Returns the NumericDocValues array for a field, or null if unavailable.</summary>
    public double[]? GetNumericDocValues(string field)
        => EnsureNumericDocValues().GetValueOrDefault(field);

    /// <summary>Returns the SortedDocValues array for a field, or null if unavailable.</summary>
    public string[]? GetSortedDocValues(string field)
        => EnsureSortedDocValues().GetValueOrDefault(field);

    /// <summary>Returns the SortedSetDocValues array for a field, or null if unavailable.</summary>
    public string[][]? GetSortedSetDocValues(string field)
        => EnsureSortedSetDocValues().GetValueOrDefault(field);

    /// <summary>Returns the SortedNumericDocValues array for a field, or null if unavailable.</summary>
    public double[][]? GetSortedNumericDocValues(string field)
        => EnsureSortedNumericDocValues().GetValueOrDefault(field);

    /// <summary>Returns the BinaryDocValues array for a field, or null if unavailable.</summary>
    public byte[][][]? GetBinaryDocValues(string field)
        => EnsureBinaryDocValues().GetValueOrDefault(field);

    /// <summary>Returns the Int64DocValues array for a field, or null if unavailable.</summary>
    public long[]? GetInt64DocValues(string field)
        => EnsureInt64DocValues().GetValueOrDefault(field);

    /// <summary>Returns the Int64SortedNumericDocValues array for a field, or null if unavailable.</summary>
    public long[][]? GetSortedInt64DocValues(string field)
        => EnsureInt64SortedDocValues().GetValueOrDefault(field);

    /// <summary>
    /// Returns <see langword="true"/> when a numeric field with the given name exists
    /// in this segment, regardless of which documents have values for it.
    /// Checks the sparse numeric index (.num), dense numeric doc values (.dvn),
    /// sorted-numeric doc values (.dsn), and the 64-bit integer equivalents.
    /// </summary>
    public bool HasNumericField(string field)
    {
        if (EnsureNumericIndex().ContainsKey(field))
            return true;
        if (EnsureNumericDocValues().ContainsKey(field))
            return true;
        if (EnsureSortedNumericDocValues().ContainsKey(field))
            return true;
        if (EnsureInt64Index().ContainsKey(field))
            return true;
        if (EnsureInt64DocValues().ContainsKey(field))
            return true;
        if (EnsureInt64SortedDocValues().ContainsKey(field))
            return true;
        return false;
    }

    /// <summary>
    /// Returns all document IDs that have a numeric value in the given field within the specified range.
    /// </summary>
    public List<(int DocId, double Value)> GetNumericRange(string field, double min, double max)
    {
        var results = new List<(int, double)>();
        VisitNumericRange(field, min, max, (docId, value) => results.Add((docId, value)));
        return results;
    }

    internal bool VisitNumericRange(string field, double min, double max, Action<int, double> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        var bkd = EnsureBkdReader();
        if (bkd is not null && bkd.HasField(field))
        {
            bkd.VisitRange(field, min, max, (docId, value) =>
            {
                if (_liveDocs is null || IsLive(docId))
                    visitor(docId, value);
            });
            return true;
        }

        var numericIndex = EnsureNumericIndex();
        if (numericIndex.TryGetValue(field, out var fieldMap))
        {
            foreach (var (docId, value) in fieldMap)
            {
                if (value >= min && value <= max && IsLive(docId))
                    visitor(docId, value);
            }

            return true;
        }

        var numericDocValues = EnsureNumericDocValues();
        if (!numericDocValues.TryGetValue(field, out var values))
            return false;

        RoaringBitmap? presenceBitmap = null;
        _numericDocValuesPresence?.TryGetValue(field, out presenceBitmap);
        for (int docId = 0; docId < values.Length; docId++)
        {
            if (!IsLive(docId))
                continue;

            if (presenceBitmap is not null && !presenceBitmap.Contains(docId))
                continue;

            double value = values[docId];
            if (value >= min && value <= max)
                visitor(docId, value);
        }

        return true;
    }

    /// <summary>
    /// Returns all document IDs whose numeric point value is equal to any value in the supplied set.
    /// </summary>
    public List<(int DocId, double Value)> GetNumericPointsInSet(string field, IReadOnlySet<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var results = new List<(int, double)>();
        if (values.Count == 0)
            return results;

        var bkd = EnsureBkdReader();
        if (bkd is not null && bkd.HasField(field))
        {
            try
            {
                var raw = bkd.ExactSetQuery(field, values);
                if (_liveDocs is null)
                    return raw;

                results.Capacity = raw.Count;
                foreach (var hit in raw)
                {
                    if (IsLive(hit.DocId))
                        results.Add(hit);
                }
                return results;
            }
            catch (EndOfStreamException)
            {
                // BKD file is corrupt or truncated — fall back to numeric index.
            }
        }

        var numericIndex = EnsureNumericIndex();
        if (!numericIndex.TryGetValue(field, out var fieldMap))
            return results;

        foreach (var (docId, value) in fieldMap)
        {
            if (values.Contains(value) && IsLive(docId))
                results.Add((docId, value));
        }

        return results;
    }

    /// <summary>
    /// Returns all document IDs that have a 64-bit integer value in the given field within the specified range.
    /// </summary>
    public List<(int DocId, long Value)> GetInt64Range(string field, long min, long max)
    {
        var results = new List<(int, long)>();
        VisitInt64Range(field, min, max, (docId, value) => results.Add((docId, value)));
        return results;
    }

    internal bool VisitInt64Range(string field, long min, long max, Action<int, long> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        var bkd = EnsureInt64BkdReader();
        if (bkd is not null && bkd.HasField(field))
        {
            bkd.VisitRange(field, min, max, (docId, value) =>
            {
                if (_liveDocs is null || IsLive(docId))
                    visitor(docId, value);
            });
            return true;
        }

        var int64Index = EnsureInt64Index();
        if (int64Index.TryGetValue(field, out var fieldMap))
        {
            foreach (var (docId, value) in fieldMap)
            {
                if (value >= min && value <= max && IsLive(docId))
                    visitor(docId, value);
            }

            return true;
        }

        var int64DocValues = EnsureInt64DocValues();
        if (!int64DocValues.TryGetValue(field, out var values))
            return false;

        RoaringBitmap? presenceBitmap = null;
        _int64DocValuesPresence?.TryGetValue(field, out presenceBitmap);
        for (int docId = 0; docId < values.Length; docId++)
        {
            if (!IsLive(docId))
                continue;

            if (presenceBitmap is not null && !presenceBitmap.Contains(docId))
                continue;

            long value = values[docId];
            if (value >= min && value <= max)
                visitor(docId, value);
        }

        return true;
    }

    /// <summary>
    /// Returns all document IDs whose 64-bit integer point value is equal to any value in the supplied set.
    /// </summary>
    public List<(int DocId, long Value)> GetInt64PointsInSet(string field, IReadOnlySet<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var results = new List<(int, long)>();
        if (values.Count == 0)
            return results;

        var bkd = EnsureInt64BkdReader();
        if (bkd is not null && bkd.HasField(field))
        {
            try
            {
                var raw = bkd.ExactSetQuery(field, values);
                if (_liveDocs is null)
                    return raw;

                results.Capacity = raw.Count;
                foreach (var hit in raw)
                {
                    if (IsLive(hit.DocId))
                        results.Add(hit);
                }
                return results;
            }
            catch (EndOfStreamException)
            {
                // BKD file is corrupt or truncated — fall back to the numeric index.
            }
        }

        var int64Index = EnsureInt64Index();
        if (!int64Index.TryGetValue(field, out var fieldMap))
            return results;

        foreach (var (docId, value) in fieldMap)
        {
            if (values.Contains(value) && IsLive(docId))
                results.Add((docId, value));
        }

        return results;
    }

    /// <summary>Returns <see langword="true"/> when the document contains at least one value for the named field.</summary>
    public bool HasFieldValue(string field, int docId)
    {
        if (TryGetFieldLengths(field, out var lengths) &&
            (uint)docId < (uint)lengths.Length &&
            lengths[docId] > 0)
        {
            return true;
        }

        if (TryGetNumericValue(field, docId, out _) ||
            TryGetInt64Value(field, docId, out _) ||
            TryGetSortedDocValue(field, docId, out _) ||
            TryGetSortedSetDocValues(field, docId, out var sortedSetValues) && sortedSetValues.Count > 0 ||
            TryGetSortedNumericDocValues(field, docId, out var sortedNumericValues) && sortedNumericValues.Count > 0 ||
            TryGetSortedInt64DocValues(field, docId, out var sortedInt64Values) && sortedInt64Values.Count > 0 ||
            TryGetBinaryDocValues(field, docId, out var binaryValues) && binaryValues.Count > 0)
        {
            return true;
        }

        if (_vectorPaths.ContainsKey(field) && GetVector(field, docId) is { Length: > 0 })
            return true;

        return _storedReader is not null && _storedReader.HasField(docId, field);
    }

    /// <summary>
    /// Lazily opens the BKD reader for this segment if a .bkd file is present.
    /// Returns null when there is no BKD file or it cannot be opened.
    /// </summary>
    private Codecs.Bkd.BKDReader? EnsureBkdReader()
    {
        if (Volatile.Read(ref _bkdReaderLoaded)) return _bkdReader;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_bkdReaderLoaded) return _bkdReader;

            var bkdPath = _basePath + ".bkd";
            if (File.Exists(bkdPath))
            {
                try
                {
                    _bkdReader = Codecs.Bkd.BKDReader.Open(bkdPath);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException)
                {
                    // Corrupt or unreadable .bkd: fall back to the linear scan path.
                    _bkdReader = null;
                }
            }
            Volatile.Write(ref _bkdReaderLoaded, true);
        }
        return _bkdReader;
    }

    /// <summary>
    /// Lazily opens the 64-bit integer BKD reader for this segment if a .bkdl file is present.
    /// Returns null when there is no file or it cannot be opened.
    /// </summary>
    private Codecs.Bkd.Int64BKDReader? EnsureInt64BkdReader()
    {
        if (Volatile.Read(ref _int64BkdReaderLoaded)) return _int64BkdReader;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_int64BkdReaderLoaded) return _int64BkdReader;

            var bkdPath = _basePath + ".bkdl";
            if (File.Exists(bkdPath))
            {
                try
                {
                    _int64BkdReader = Codecs.Bkd.Int64BKDReader.Open(bkdPath);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException)
                {
                    // Corrupt or unreadable .bkdl: fall back to the linear scan path.
                    _int64BkdReader = null;
                }
            }
            Volatile.Write(ref _int64BkdReaderLoaded, true);
        }
        return _int64BkdReader;
    }

    /// <summary>Returns whether this segment has vector data.</summary>
    public bool HasVectors => _vectorPaths.Count > 0;

    /// <summary>Reads the vector for a given document from the first available vector field (legacy convenience).</summary>
    public float[]? GetVector(int docId)
    {
        foreach (var fieldName in _vectorPaths.Keys)
        {
            var result = ReadVectorFromField(fieldName, docId);
            if (result is not null)
                return result;
        }
        return null;
    }

    /// <summary>Reads the vector for a given document on the named vector field.</summary>
    public float[]? GetVector(string fieldName, int docId)
    {
        if (ReadVectorFromField(fieldName, docId) is { } vec)
            return vec;
        if (string.IsNullOrEmpty(fieldName) && _vectorPaths.Count == 1)
            return GetVector(docId);
        return null;
    }

    private float[]? ReadVectorFromField(string fieldName, int docId)
    {
        if (_vectorReaders.TryGetValue(fieldName, out var vr))
            return vr.ReadVector(docId);
        if (_quantisedVectorReaders.TryGetValue(fieldName, out var qr))
            return qr.ReadVector(docId);

        lock (_hnswLoadLock)
        {
            if (_vectorReaders.TryGetValue(fieldName, out vr))
                return vr.ReadVector(docId);
            if (_quantisedVectorReaders.TryGetValue(fieldName, out qr))
                return qr.ReadVector(docId);
            if (!_vectorPaths.TryGetValue(fieldName, out var path))
                return null;

            if (_vectorQuantisation.TryGetValue(fieldName, out var q) && q != VectorQuantisation.None)
            {
                qr = QuantisedVectorReader.Open(path);
                _quantisedVectorReaders[fieldName] = qr;
                return qr.ReadVector(docId);
            }

            vr = VectorReader.Open(path);
            _vectorReaders[fieldName] = vr;
            return vr.ReadVector(docId);
        }
    }

    /// <summary>Returns the field names with vector data in this segment.</summary>
    public IReadOnlyCollection<string> VectorFieldNames => _vectorPaths.Keys;

    /// <summary>
    /// Returns the (lazy-loaded) HNSW graph for the given vector field, or null if no graph exists.
    /// Thread-safe; the first caller materialises the graph and subsequent callers reuse it.
    /// </summary>
    internal HnswGraph? GetHnswGraph(string fieldName)
    {
        if (_hnswGraphs.TryGetValue(fieldName, out var cached)) return cached;
        lock (_hnswLoadLock)
        {
            if (_hnswGraphs.TryGetValue(fieldName, out cached)) return cached;
            var path = VectorFilePaths.HnswFile(_basePath, fieldName);
            HnswGraph? graph = null;

            if (File.Exists(path))
            {
                IVectorSource? src = null;
                if (_vectorReaders.TryGetValue(fieldName, out var vr))
                    src = new VectorReaderSource(vr);
                else if (_quantisedVectorReaders.TryGetValue(fieldName, out var qr))
                    src = new QuantisedVectorSource(qr);
                else if (_vectorPaths.TryGetValue(fieldName, out var vecPath))
                {
                    if (_vectorQuantisation.TryGetValue(fieldName, out var q) && q != VectorQuantisation.None)
                    {
                        qr = QuantisedVectorReader.Open(vecPath);
                        _quantisedVectorReaders[fieldName] = qr;
                        src = new QuantisedVectorSource(qr);
                    }
                    else
                    {
                        vr = VectorReader.Open(vecPath);
                        _vectorReaders[fieldName] = vr;
                        src = new VectorReaderSource(vr);
                    }
                }

                if (src is not null)
                {
                    bool? expectedNormalised = _info.VectorFields
                        .FirstOrDefault(vf => vf.FieldName == fieldName)?.Normalised;
                    graph = HnswReader.Read(path, src, expectedNormalised);
                }
            }
            _hnswGraphs[fieldName] = graph;
            return graph;
        }
    }

    private static Dictionary<string, Dictionary<int, double>> ReadNumericIndex(string filePath)
    {
        var result = new Dictionary<string, Dictionary<int, double>>();
        using var fs = FileOpenRetry.OpenReadDelete(filePath);
        using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        int fieldCount = reader.ReadInt32();
        for (int f = 0; f < fieldCount; f++)
        {
            string fieldName = reader.ReadString();
            int entryCount = reader.ReadInt32();
            var fieldMap = new Dictionary<int, double>(entryCount);
            for (int e = 0; e < entryCount; e++)
            {
                int docId = reader.ReadInt32();
                double value = reader.ReadDouble();
                fieldMap[docId] = value;
            }
            result[fieldName] = fieldMap;
        }
        return result;
    }

    private static Dictionary<string, Dictionary<int, long>> ReadInt64Index(string filePath)
    {
        var result = new Dictionary<string, Dictionary<int, long>>();
        using var fs = FileOpenRetry.OpenReadDelete(filePath);
        using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        int fieldCount = reader.ReadInt32();
        for (int f = 0; f < fieldCount; f++)
        {
            string fieldName = reader.ReadString();
            int entryCount = reader.ReadInt32();
            var fieldMap = new Dictionary<int, long>(entryCount);
            for (int e = 0; e < entryCount; e++)
            {
                int docId = reader.ReadInt32();
                long value = reader.ReadInt64();
                fieldMap[docId] = value;
            }
            result[fieldName] = fieldMap;
        }
        return result;
    }
}
