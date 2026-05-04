using Rowles.LeanLucene.Codecs.DocValues;
using Rowles.LeanLucene.Codecs.Hnsw;
using Rowles.LeanLucene.Codecs.Vectors;

namespace Rowles.LeanLucene.Index.Segment;

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

    /// <summary>Returns the NumericDocValues array for a field, or null if unavailable.</summary>
    public double[]? GetNumericDocValues(string field)
        => EnsureNumericDocValues().GetValueOrDefault(field);

    /// <summary>Returns the SortedDocValues array for a field, or null if unavailable.</summary>
    public string[]? GetSortedDocValues(string field)
        => EnsureSortedDocValues().GetValueOrDefault(field);

    /// <summary>
    /// Returns all document IDs that have a numeric value in the given field within the specified range.
    /// </summary>
    public List<(int DocId, double Value)> GetNumericRange(string field, double min, double max)
    {
        var results = new List<(int, double)>();

        // Fast path: use the BKD tree if one is available for this segment.
        var bkd = EnsureBkdReader();
        if (bkd is not null && bkd.HasField(field))
        {
            var raw = bkd.RangeQuery(field, min, max);
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

        // Fallback: linear scan of the sparse numeric index. Used for legacy segments
        // written before BKD was wired or when the .bkd file is missing for this field.
        var numericIndex = EnsureNumericIndex();
        if (!numericIndex.TryGetValue(field, out var fieldMap))
            return results;

        foreach (var (docId, value) in fieldMap)
        {
            if (value >= min && value <= max && IsLive(docId))
                results.Add((docId, value));
        }
        return results;
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

    /// <summary>Returns whether this segment has vector data.</summary>
    public bool HasVectors => _vectorPaths.Count > 0;

    /// <summary>Reads the vector for a given document from the first available vector field (legacy convenience).</summary>
    public float[]? GetVector(int docId)
    {
        foreach (var fieldName in _vectorPaths.Keys)
            return EnsureVectorReader(fieldName)?.ReadVector(docId);
        return null;
    }

    /// <summary>Reads the vector for a given document on the named vector field.</summary>
    public float[]? GetVector(string fieldName, int docId)
    {
        if (EnsureVectorReader(fieldName) is { } r)
            return r.ReadVector(docId);
        if (string.IsNullOrEmpty(fieldName) && _vectorPaths.Count == 1)
            return GetVector(docId);
        return null;
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
            var vr = EnsureVectorReaderNoLock(fieldName);
            if (File.Exists(path) && vr is not null)
            {
                var src = new VectorReaderSource(vr);
                bool? expectedNormalised = _info.VectorFields
                    .FirstOrDefault(vf => vf.FieldName == fieldName)?.Normalised;
                graph = HnswReader.Read(path, src, expectedNormalised);
            }
            _hnswGraphs[fieldName] = graph;
            return graph;
        }
    }

    private VectorReader? EnsureVectorReader(string fieldName)
    {
        if (_vectorReaders.TryGetValue(fieldName, out var reader))
            return reader;

        lock (_hnswLoadLock)
        {
            return EnsureVectorReaderNoLock(fieldName);
        }
    }

    private VectorReader? EnsureVectorReaderNoLock(string fieldName)
    {
        if (_vectorReaders.TryGetValue(fieldName, out var reader))
            return reader;
        if (!_vectorPaths.TryGetValue(fieldName, out var path))
            return null;

        reader = VectorReader.Open(path);
        _vectorReaders[fieldName] = reader;
        return reader;
    }

    private static Dictionary<string, Dictionary<int, double>> ReadNumericIndex(string filePath)
    {
        var result = new Dictionary<string, Dictionary<int, double>>();
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
}
