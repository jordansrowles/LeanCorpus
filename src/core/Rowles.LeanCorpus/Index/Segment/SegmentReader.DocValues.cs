using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// DocValues and numeric index-related methods for SegmentReaderState.
/// </summary>
internal sealed partial class SegmentReaderState
{
    /// <summary>Lazy-loads the numeric index (.num) for range queries.</summary>
    private Dictionary<string, Dictionary<int, double>> EnsureNumericIndex()
    {
        return LazyInitializer.EnsureInitialized(ref _numericIndex, ref _lazyInitLock, () =>
        {
            return _files.Exists(".num")
                ? ReadNumericIndex(_files.OpenInput(".num"))
                : new Dictionary<string, Dictionary<int, double>>();
        })!;
    }

    private Dictionary<string, NumericDocValuesColumn> EnsureNumericDocValueColumns()
        => EnsureDocValuesColumns(ref _numericDocValueColumns, ".dvn", NumericDocValuesReader.OpenColumns);

    /// <summary>Lazy-loads the 64-bit integer index (.numl) for range queries.</summary>
    private Dictionary<string, Dictionary<int, long>> EnsureInt64Index()
    {
        return LazyInitializer.EnsureInitialized(ref _int64Index, ref _lazyInitLock, () =>
        {
            return _files.Exists(".numl")
                ? ReadInt64Index(_files.OpenInput(".numl"))
                : new Dictionary<string, Dictionary<int, long>>();
        })!;
    }

    /// <summary>Materialises the compatibility array only when a caller explicitly requests it.</summary>
    private Dictionary<string, double[]> EnsureNumericDocValues()
    {
        var columns = EnsureNumericDocValueColumns();
        if (_numericDocValues is not null)
            return _numericDocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_numericDocValues is not null)
                return _numericDocValues;
            _numericDocValues = MaterialiseColumns(columns, static column => column.Materialise());
            _numericDocValuesPresence = columns.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value.Presence,
                StringComparer.Ordinal);
            return _numericDocValues;
        }
    }

    private Dictionary<string, Int64DocValuesColumn> EnsureInt64DocValueColumns()
        => EnsureDocValuesColumns(ref _int64DocValueColumns, ".dvnl", Int64DocValuesReader.OpenColumns);

    /// <summary>Materialises the compatibility array only when a caller explicitly requests it.</summary>
    private Dictionary<string, long[]> EnsureInt64DocValues()
    {
        var columns = EnsureInt64DocValueColumns();
        if (_int64DocValues is not null)
            return _int64DocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_int64DocValues is not null)
                return _int64DocValues;
            _int64DocValues = MaterialiseColumns(columns, static column => column.Materialise());
            _int64DocValuesPresence = columns.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value.Presence,
                StringComparer.Ordinal);
            return _int64DocValues;
        }
    }

    private Dictionary<string, Int64SortedNumericDocValuesColumn> EnsureInt64SortedDocValueColumns()
        => EnsureDocValuesColumns(ref _int64SortedDocValueColumns, ".dsnl", Int64SortedNumericDocValuesReader.OpenColumns);

    private Dictionary<string, long[][]> EnsureInt64SortedDocValues()
    {
        var columns = EnsureInt64SortedDocValueColumns();
        if (_int64SortedDocValues is not null)
            return _int64SortedDocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            _int64SortedDocValues ??= MaterialiseColumns(columns, static column => column.Materialise());
            return _int64SortedDocValues;
        }
    }

    private Dictionary<string, SortedDocValuesColumn> EnsureSortedDocValueColumns()
        => EnsureDocValuesColumns(ref _sortedDocValueColumns, ".dvs", SortedDocValuesReader.OpenColumns);

    /// <summary>Materialises the compatibility array only when a caller explicitly requests it.</summary>
    private Dictionary<string, string[]> EnsureSortedDocValues()
    {
        var columns = EnsureSortedDocValueColumns();
        if (_sortedDocValues is not null)
            return _sortedDocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_sortedDocValues is not null)
                return _sortedDocValues;
            _sortedDocValues = MaterialiseColumns(columns, static column => column.Materialise());
            _sortedDocValuesPresence = columns.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value.Presence,
                StringComparer.Ordinal);
            return _sortedDocValues;
        }
    }

    private Dictionary<string, SortedSetDocValuesColumn> EnsureSortedSetDocValueColumns()
        => EnsureDocValuesColumns(ref _sortedSetDocValueColumns, ".dss", SortedSetDocValuesReader.OpenColumns);

    private Dictionary<string, string[][]> EnsureSortedSetDocValues()
    {
        var columns = EnsureSortedSetDocValueColumns();
        if (_sortedSetDocValues is not null)
            return _sortedSetDocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            _sortedSetDocValues ??= MaterialiseColumns(columns, static column => column.Materialise());
            return _sortedSetDocValues;
        }
    }

    private Dictionary<string, string[]> EnsureSortedDocValueTerms()
    {
        if (_sortedDocValueTerms is not null)
            return _sortedDocValueTerms;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            _sortedDocValueTerms ??= EnsureSortedDocValueColumns().ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value.CopyTerms(),
                StringComparer.Ordinal);
            return _sortedDocValueTerms;
        }
    }

    private Dictionary<string, string[]> EnsureSortedSetDocValueTerms()
    {
        if (_sortedSetDocValueTerms is not null)
            return _sortedSetDocValueTerms;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            _sortedSetDocValueTerms ??= EnsureSortedSetDocValueColumns().ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value.CopyTerms(),
                StringComparer.Ordinal);
            return _sortedSetDocValueTerms;
        }
    }

    private Dictionary<string, SortedNumericDocValuesColumn> EnsureSortedNumericDocValueColumns()
        => EnsureDocValuesColumns(ref _sortedNumericDocValueColumns, ".dsn", SortedNumericDocValuesReader.OpenColumns);

    private Dictionary<string, double[][]> EnsureSortedNumericDocValues()
    {
        var columns = EnsureSortedNumericDocValueColumns();
        if (_sortedNumericDocValues is not null)
            return _sortedNumericDocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            _sortedNumericDocValues ??= MaterialiseColumns(columns, static column => column.Materialise());
            return _sortedNumericDocValues;
        }
    }

    private Dictionary<string, BinaryDocValuesColumn> EnsureBinaryDocValueColumns()
        => EnsureDocValuesColumns(ref _binaryDocValueColumns, ".dvb", BinaryDocValuesReader.OpenColumns);

    private Dictionary<string, byte[][][]> EnsureBinaryDocValues()
    {
        var columns = EnsureBinaryDocValueColumns();
        if (_binaryDocValues is not null)
            return _binaryDocValues;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            _binaryDocValues ??= MaterialiseColumns(columns, static column => column.Materialise());
            return _binaryDocValues;
        }
    }

    private Dictionary<string, TColumn> EnsureDocValuesColumns<TColumn>(
        ref Dictionary<string, TColumn>? columns,
        string extension,
        Func<IndexInput, Dictionary<string, TColumn>> open)
    {
        if (columns is not null)
            return columns;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (columns is not null)
                return columns;
            if (!_files.Exists(extension))
                return columns = new Dictionary<string, TColumn>(StringComparer.Ordinal);

            IndexInput input = _files.OpenInput(extension);
            try
            {
                Dictionary<string, TColumn> opened = open(input);
                _docValuesInputs.Add(input);
                return columns = opened;
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }
    }

    private static Dictionary<string, TValue> MaterialiseColumns<TColumn, TValue>(
        Dictionary<string, TColumn> columns,
        Func<TColumn, TValue> materialise)
    {
        var values = new Dictionary<string, TValue>(columns.Count, StringComparer.Ordinal);
        foreach ((string field, TColumn column) in columns)
            values.Add(field, materialise(column));
        return values;
    }

    /// <summary>Tries to get one numeric value, falling back to the packed DocValues column.</summary>
    public bool TryGetNumericValue(string field, int docId, out double value)
    {
        value = 0;
        var numericIndex = EnsureNumericIndex();
        if (numericIndex.TryGetValue(field, out var fieldMap))
            return fieldMap.TryGetValue(docId, out value);

        return EnsureNumericDocValueColumns().TryGetValue(field, out var column)
            && column.TryGetValue(docId, out value);
    }

    /// <summary>Gets the packed numeric DocValues column for a caller holding a segment lease.</summary>
    internal bool TryGetNumericDocValues(
        string field,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NumericDocValuesColumn? values)
        => EnsureNumericDocValueColumns().TryGetValue(field, out values);

    /// <summary>Tries to get one Int64 value, falling back to the packed DocValues column.</summary>
    public bool TryGetInt64Value(string field, int docId, out long value)
    {
        value = 0;
        var int64Index = EnsureInt64Index();
        if (int64Index.TryGetValue(field, out var fieldMap))
            return fieldMap.TryGetValue(docId, out value);

        return EnsureInt64DocValueColumns().TryGetValue(field, out var column)
            && column.TryGetValue(docId, out value);
    }

    public bool TryGetSortedDocValue(string field, int docId, out string value)
    {
        value = string.Empty;
        if (!EnsureSortedDocValueColumns().TryGetValue(field, out var column)
            || !column.TryGetOrdinal(docId, out _))
            return false;

        value = column.ValueCount == 0 ? string.Empty : column.GetValue(docId);
        return true;
    }

    public bool TryGetSortedDocOrdinal(string field, int docId, out int ordinal)
    {
        ordinal = -1;
        if (!EnsureSortedDocValueColumns().TryGetValue(field, out var column)
            || column.ValueCount == 0)
            return false;
        return column.TryGetOrdinal(docId, out ordinal);
    }

    public bool TryGetSortedSetDocValues(string field, int docId, out IReadOnlyList<string> values)
    {
        if (!EnsureSortedSetDocValueColumns().TryGetValue(field, out var column)
            || !column.HasValues(docId))
        {
            values = Array.Empty<string>();
            return false;
        }

        values = column.GetValues(docId);
        return true;
    }

    public bool TryGetSortedSetDocOrdinals(string field, int docId, out IReadOnlyList<int> ordinals)
    {
        if (!EnsureSortedSetDocValueColumns().TryGetValue(field, out var column)
            || !column.HasValues(docId))
        {
            ordinals = Array.Empty<int>();
            return false;
        }

        ordinals = column.GetOrdinals(docId);
        return true;
    }

    public bool TryGetSortedNumericDocValues(string field, int docId, out IReadOnlyList<double> values)
    {
        if (!EnsureSortedNumericDocValueColumns().TryGetValue(field, out var column)
            || !column.HasValues(docId))
        {
            values = Array.Empty<double>();
            return false;
        }

        values = column.GetValues(docId);
        return true;
    }

    public bool TryGetSortedInt64DocValues(string field, int docId, out IReadOnlyList<long> values)
    {
        if (!EnsureInt64SortedDocValueColumns().TryGetValue(field, out var column)
            || !column.HasValues(docId))
        {
            values = Array.Empty<long>();
            return false;
        }

        values = column.GetValues(docId);
        return true;
    }

    public bool TryGetBinaryDocValues(string field, int docId, out IReadOnlyList<byte[]> values)
    {
        if (!EnsureBinaryDocValueColumns().TryGetValue(field, out var column)
            || !column.HasValues(docId))
        {
            values = Array.Empty<byte[]>();
            return false;
        }

        values = column.GetValues(docId);
        return true;
    }

    internal bool HasNumericDocValues(string field) => EnsureNumericDocValueColumns().ContainsKey(field);
    internal bool HasInt64DocValues(string field) => EnsureInt64DocValueColumns().ContainsKey(field);
    internal bool HasSortedDocValues(string field) => EnsureSortedDocValueColumns().ContainsKey(field);
    internal bool HasSortedSetDocValues(string field) => EnsureSortedSetDocValueColumns().ContainsKey(field);
    internal bool HasSortedNumericDocValues(string field) => EnsureSortedNumericDocValueColumns().ContainsKey(field);
    internal bool HasSortedInt64DocValues(string field) => EnsureInt64SortedDocValueColumns().ContainsKey(field);
    internal bool HasBinaryDocValues(string field) => EnsureBinaryDocValueColumns().ContainsKey(field);

    internal void ValidateDocValuesDocumentCounts()
    {
        int expectedDocumentCount = _info.DocCount;
        ValidateColumnDocumentCounts(EnsureNumericDocValueColumns(), expectedDocumentCount);
        ValidateColumnDocumentCounts(EnsureInt64DocValueColumns(), expectedDocumentCount);
        ValidateColumnDocumentCounts(EnsureSortedDocValueColumns(), expectedDocumentCount);
        ValidateColumnDocumentCounts(EnsureSortedSetDocValueColumns(), expectedDocumentCount);
        ValidateColumnDocumentCounts(EnsureSortedNumericDocValueColumns(), expectedDocumentCount);
        ValidateColumnDocumentCounts(EnsureInt64SortedDocValueColumns(), expectedDocumentCount);
        ValidateColumnDocumentCounts(EnsureBinaryDocValueColumns(), expectedDocumentCount);
    }

    private static void ValidateColumnDocumentCounts<TColumn>(
        Dictionary<string, TColumn> columns,
        int expectedDocumentCount)
    {
        foreach ((string field, TColumn column) in columns)
        {
            int documentCount = column switch
            {
                NumericDocValuesColumn numeric => numeric.DocumentCount,
                Int64DocValuesColumn int64 => int64.DocumentCount,
                SortedDocValuesColumn sorted => sorted.DocumentCount,
                SortedSetDocValuesColumn sortedSet => sortedSet.DocumentCount,
                SortedNumericDocValuesColumn sortedNumeric => sortedNumeric.DocumentCount,
                Int64SortedNumericDocValuesColumn int64SortedNumeric => int64SortedNumeric.DocumentCount,
                BinaryDocValuesColumn binary => binary.DocumentCount,
                _ => throw new InvalidOperationException($"Unsupported DocValues column type '{typeof(TColumn)}'."),
            };

            if (documentCount != expectedDocumentCount)
                throw new InvalidDataException(
                    $"DocValues field '{field}' contains {documentCount} documents but the segment declares {expectedDocumentCount}.");
        }
    }

    /// <summary>Returns whether a binary DocValues field has at least one value for every segment document.</summary>
    internal bool HasBinaryDocValuesForEveryDocument(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!EnsureBinaryDocValueColumns().TryGetValue(field, out var column)
            || column.DocumentCount != _info.DocCount)
            return false;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            _binaryDocValuesFullCoverage ??= new Dictionary<string, bool>(StringComparer.Ordinal);
            if (_binaryDocValuesFullCoverage.TryGetValue(field, out bool hasFullCoverage))
                return hasFullCoverage;

            hasFullCoverage = column.HasValuesForEveryDocument();
            _binaryDocValuesFullCoverage.Add(field, hasFullCoverage);
            return hasFullCoverage;
        }
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

    /// <summary>Returns the sorted local term dictionary for a field, or null if unavailable.</summary>
    public string[]? GetSortedDocValueTerms(string field)
        => EnsureSortedDocValueTerms().GetValueOrDefault(field);

    /// <summary>Returns the sorted-set local term dictionary for a field, or null if unavailable.</summary>
    public string[]? GetSortedSetDocValueTerms(string field)
        => EnsureSortedSetDocValueTerms().GetValueOrDefault(field);

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
        if (HasNumericDocValues(field))
            return true;
        if (HasSortedNumericDocValues(field))
            return true;
        if (EnsureInt64Index().ContainsKey(field))
            return true;
        if (HasInt64DocValues(field))
            return true;
        if (HasSortedInt64DocValues(field))
            return true;
        return false;
    }

    /// <summary>Returns whether a sparse double numeric index contains a field.</summary>
    internal bool HasNumericIndex(string field) => EnsureNumericIndex().ContainsKey(field);

    /// <summary>Returns whether a sparse Int64 numeric index contains a field.</summary>
    internal bool HasInt64Index(string field) => EnsureInt64Index().ContainsKey(field);

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
                if (LiveDocuments is null || IsLive(docId))
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

        var numericDocValues = EnsureNumericDocValueColumns();
        if (!numericDocValues.TryGetValue(field, out var column))
            return false;

        for (int docId = 0; docId < column.DocumentCount; docId++)
        {
            if (!IsLive(docId))
                continue;

            if (!column.TryGetValue(docId, out double value))
                continue;

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
                if (LiveDocuments is null)
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
                // BKD file is corrupt or truncated; fall back to numeric index.
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
                if (LiveDocuments is null || IsLive(docId))
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

        var int64DocValues = EnsureInt64DocValueColumns();
        if (!int64DocValues.TryGetValue(field, out var column))
            return false;

        for (int docId = 0; docId < column.DocumentCount; docId++)
        {
            if (!IsLive(docId))
                continue;

            if (!column.TryGetValue(docId, out long value))
                continue;

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
                if (LiveDocuments is null)
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
                // BKD file is corrupt or truncated; fall back to the numeric index.
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

        return StoredReader is not null && StoredReader.HasField(docId, field);
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

            if (_files.Exists(".bkd"))
            {
                try
                {
                    _bkdReader = Codecs.Bkd.BKDReader.Open(_files.OpenInput(".bkd"));
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

            if (_files.Exists(".bkdl"))
            {
                try
                {
                    _int64BkdReader = Codecs.Bkd.Int64BKDReader.Open(_files.OpenInput(".bkdl"));
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

    internal bool TryCopyVectorTo(string fieldName, int docId, Span<float> destination)
    {
        if (string.IsNullOrEmpty(fieldName) && _vectorPaths.Count == 1)
            fieldName = _vectorPaths.Keys.First();

        if (_vectorReaders.TryGetValue(fieldName, out var vectorReader))
        {
            vectorReader.ReadVector(docId, destination);
            return true;
        }
        if (_quantisedVectorReaders.TryGetValue(fieldName, out var quantisedReader))
        {
            quantisedReader.ReadVector(docId, destination);
            return true;
        }

        lock (_hnswLoadLock)
        {
            if (_vectorReaders.TryGetValue(fieldName, out vectorReader))
            {
                vectorReader.ReadVector(docId, destination);
                return true;
            }
            if (_quantisedVectorReaders.TryGetValue(fieldName, out quantisedReader))
            {
                quantisedReader.ReadVector(docId, destination);
                return true;
            }
            if (!_vectorPaths.TryGetValue(fieldName, out var path))
            {
                destination.Clear();
                return false;
            }

            if (_vectorQuantisation.TryGetValue(fieldName, out var quantisation)
                && quantisation != VectorQuantisation.None)
            {
                quantisedReader = QuantisedVectorReader.Open(_files.OpenInput(path));
                _quantisedVectorReaders[fieldName] = quantisedReader;
                quantisedReader.ReadVector(docId, destination);
            }
            else
            {
                vectorReader = VectorReader.Open(_files.OpenInput(path));
                _vectorReaders[fieldName] = vectorReader;
                vectorReader.ReadVector(docId, destination);
            }
            return true;
        }
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
                qr = QuantisedVectorReader.Open(_files.OpenInput(path));
                _quantisedVectorReaders[fieldName] = qr;
                return qr.ReadVector(docId);
            }

            vr = VectorReader.Open(_files.OpenInput(path));
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
            string hnswExtension = Path.GetFileName(path)[_info.SegmentId.Length..];
            HnswGraph? graph = null;

            if (_files.Exists(hnswExtension))
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
                        qr = QuantisedVectorReader.Open(_files.OpenInput(vecPath));
                        _quantisedVectorReaders[fieldName] = qr;
                        src = new QuantisedVectorSource(qr);
                    }
                    else
                    {
                        vr = VectorReader.Open(_files.OpenInput(vecPath));
                        _vectorReaders[fieldName] = vr;
                        src = new VectorReaderSource(vr);
                    }
                }

                if (src is not null)
                {
                    bool? expectedNormalised = _info.VectorFields
                        .FirstOrDefault(vf => vf.FieldName == fieldName)?.Normalised;
                    graph = HnswReader.Read(_files.OpenInput(hnswExtension), src, expectedNormalised, docIdRemap: null);
                }
            }
            _hnswGraphs[fieldName] = graph;
            return graph;
        }
    }

    private static Dictionary<string, Dictionary<int, double>> ReadNumericIndex(string filePath)
    {
        using var input = new IndexInput(filePath);
        return ReadNumericIndex(input);
    }

    private static Dictionary<string, Dictionary<int, double>> ReadNumericIndex(IndexInput input)
        => NumericIndexCodec.ReadDouble(input);

    private static Dictionary<string, Dictionary<int, long>> ReadInt64Index(string filePath)
    {
        using var input = new IndexInput(filePath);
        return ReadInt64Index(input);
    }

    private static Dictionary<string, Dictionary<int, long>> ReadInt64Index(IndexInput input)
        => NumericIndexCodec.ReadInt64(input);
}
