using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Rowles.LeanLucene.Codecs.Hnsw;
using Rowles.LeanLucene.Codecs.Vectors;
using Rowles.LeanLucene.Codecs.TermVectors;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Index.Segment;

/// <summary>
/// Reads a single immutable segment from disc via MMapDirectory.
/// </summary>
public sealed partial class SegmentReader : IDisposable
{
    private readonly MMapDirectory _directory;
    private readonly SegmentInfo _info;
    private readonly TermDictionaryReader _dicReader;
    private readonly IndexInput _posInput;
    private readonly byte _postingsVersion;
    private readonly StoredFieldsReader? _storedReader;
    private readonly FrozenDictionary<string, byte[]> _fieldNorms;
    private readonly FrozenDictionary<string, int[]> _fieldLengthsPerField;
    private readonly Dictionary<string, string> _vectorPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VectorReader> _vectorReaders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HnswGraph?> _hnswGraphs = new(StringComparer.Ordinal);
    private readonly object _hnswLoadLock = new();
    private LiveDocs? _liveDocs;

    private const int MaxTermOffsetCacheSize = 1024;
    private readonly TermOffsetCache _termOffsetCache = new(MaxTermOffsetCacheSize);

    // Lazy-loaded Stage 2 features (thread-safe via LazyInitializer)
    private Dictionary<string, Dictionary<int, double>>? _numericIndex;
    private Dictionary<string, double[]>? _numericDocValues;
    private Dictionary<string, Util.RoaringBitmap?>? _numericDocValuesPresence;
    private Dictionary<string, string[]>? _sortedDocValues;
    private Dictionary<string, Util.RoaringBitmap?>? _sortedDocValuesPresence;
    private TermVectorsReader? _termVectorsReader;
    private Codecs.Bkd.BKDReader? _bkdReader;
    private bool _bkdReaderLoaded;
    private object? _lazyInitLock;
    private readonly string _basePath;
    private ParentBitSet? _parentBitSet;
    private bool _parentBitSetLoaded;

    /// <summary>Gets or sets the document base offset for this reader within the global document namespace.</summary>
    public int DocBase { get; set; }

    /// <summary>Gets the segment metadata for this reader.</summary>
    public SegmentInfo Info => _info;

    /// <summary>Gets the total number of documents in this segment, including deleted documents.</summary>
    public int MaxDoc => _info.DocCount;

    /// <summary>
    /// Initialises a new <see cref="SegmentReader"/> for the given segment.
    /// Opens all required files and validates the segment's on-disk data.
    /// </summary>
    /// <param name="directory">The directory containing the segment files.</param>
    /// <param name="info">The segment metadata.</param>
    /// <exception cref="FileNotFoundException">Thrown if required segment files are missing.</exception>
    /// <exception cref="InvalidDataException">Thrown if segment files contain corrupted or incompatible data.</exception>
    public SegmentReader(MMapDirectory directory, SegmentInfo info)
    {
        _directory = directory;
        _info = info;
        _basePath = Path.Combine(directory.DirectoryPath, info.SegmentId);

        ValidateSegmentFiles(_basePath, info.DocCount);
        _dicReader = TermDictionaryReader.Open(_basePath + ".dic");
        _posInput = directory.OpenInput(info.SegmentId + ".pos");
        _postingsVersion = PostingsEnum.ValidateFileHeader(_posInput);

        var fdtPath = _basePath + ".fdt";
        var fdxPath = _basePath + ".fdx";
        if (File.Exists(fdtPath) && File.Exists(fdxPath))
            _storedReader = StoredFieldsReader.Open(fdtPath, fdxPath);

        var delPath = info.DelGeneration.HasValue
            ? _basePath + $"_gen_{info.DelGeneration.Value}.del"
            : _basePath + ".del";
        if (File.Exists(delPath))
            _liveDocs = LiveDocs.Deserialise(delPath, info.DocCount);

        // Load per-field norms
        _fieldNorms = NormsReader.Read(_basePath + ".nrm").ToFrozenDictionary(StringComparer.Ordinal);

        // Prefer exact field lengths from .fln; fall back to quantised norms
        var exactLengths = FieldLengthReader.TryRead(_basePath + ".fln");
        if (exactLengths is not null)
        {
            _fieldLengthsPerField = exactLengths.ToFrozenDictionary(StringComparer.Ordinal);
        }
        else
        {
            var tempLengths = new Dictionary<string, int[]>(_fieldNorms.Count, StringComparer.Ordinal);
            foreach (var (fieldName, norms) in _fieldNorms)
            {
                var fieldLengths = new int[norms.Length];
                for (int i = 0; i < norms.Length; i++)
                {
                    float n = norms[i] / 255f;
                    fieldLengths[i] = n <= 0f ? 1 : Math.Max(1, (int)MathF.Round(1.0f / n - 1.0f));
                }
                tempLengths[fieldName] = fieldLengths;
            }
            _fieldLengthsPerField = tempLengths.ToFrozenDictionary(StringComparer.Ordinal);
        }

        // Vector fields: record paths only. Opening the mmap-backed readers is deferred
        // until a VectorQuery or explicit vector read actually needs them.
        if (info.VectorFields.Count > 0)
        {
            foreach (var vf in info.VectorFields)
            {
                var perFieldVecPath = VectorFilePaths.VectorFile(_basePath, vf.FieldName);
                if (File.Exists(perFieldVecPath))
                    _vectorPaths[vf.FieldName] = perFieldVecPath;
            }
        }
        else
        {
            // Legacy single-vector segment: pre-multi-vector layout.
            var legacyVecPath = _basePath + ".vec";
            if (File.Exists(legacyVecPath))
                _vectorPaths[string.Empty] = legacyVecPath;
        }

        // Stage 2 features: numeric index, numeric doc values, and sorted doc values are now lazy-loaded
        // to avoid startup regression for simple TermQuery and BooleanQuery operations
    }

    /// <summary>
    /// Returns <see langword="true"/> if the document with the given ID has not been deleted.
    /// </summary>
    /// <param name="docId">The local (segment-relative) document ID to check.</param>
    /// <returns><see langword="true"/> if the document is live; <see langword="false"/> if deleted.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsLive(int docId) => _liveDocs?.IsLive(docId) ?? true;

    /// <summary>True when this segment has no deleted documents, allowing callers to skip per-doc IsLive checks.</summary>
    public bool HasDeletions => _liveDocs is not null;

    /// <summary>
    /// Returns the parent bitset for block-join indexing, or null if this segment
    /// has no block documents.
    /// </summary>
    internal ParentBitSet? GetParentBitSet()
    {
        if (Volatile.Read(ref _parentBitSetLoaded)) return _parentBitSet;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_parentBitSetLoaded) return _parentBitSet;
            var pbsPath = _basePath + ".pbs";
            if (File.Exists(pbsPath))
                _parentBitSet = ParentBitSet.ReadFrom(pbsPath);
            Volatile.Write(ref _parentBitSetLoaded, true);
        }
        return _parentBitSet;
    }

    /// <summary>Returns the quantised norm value for a document in a specific field (0..1 range).</summary>
    public float GetNorm(int docId, string field)
    {
        if (_fieldNorms.TryGetValue(field, out var norms) && (uint)docId < (uint)norms.Length)
            return norms[docId] / 255f;
        return 0f;
    }

    /// <summary>Returns the quantised norm value for a document using the first available field.</summary>
    public float GetNorm(int docId)
    {
        foreach (var norms in _fieldNorms.Values)
        {
            if ((uint)docId < (uint)norms.Length)
                return norms[docId] / 255f;
        }
        return 0f;
    }

    /// <summary>
    /// Returns an approximate field length for BM25 for a specific field, derived from the stored norm.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetFieldLength(int docId, string field)
    {
        if (_fieldLengthsPerField.TryGetValue(field, out var fieldLengths))
            return (uint)docId < (uint)fieldLengths.Length ? fieldLengths[docId] : 1;
        return 1;
    }

    /// <summary>
    /// Retrieves the raw field-length array for a given field, allowing callers to
    /// resolve the array once and index by docId directly in tight loops.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetFieldLengths(string field, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out int[]? lengths)
    {
        return _fieldLengthsPerField.TryGetValue(field, out lengths);
    }

    /// <summary>
    /// Returns an approximate field length using the first available field (for non-field-specific queries).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetFieldLength(int docId)
    {
        foreach (var fieldLengths in _fieldLengthsPerField.Values)
        {
            if ((uint)docId < (uint)fieldLengths.Length)
                return fieldLengths[docId];
        }
        return 1;
    }

    /// <summary>
    /// Returns term vectors for a document, or null if term vectors are not stored for this segment.
    /// Lazily opens the .tvd/.tvx files on first access.
    /// </summary>
    public Dictionary<string, List<TermVectorEntry>>? GetTermVectors(int docId)
    {
        var reader = EnsureTermVectorsReader();
        return reader?.GetTermVector(docId);
    }

    /// <summary>Whether this segment has term vector files.</summary>
    public bool HasTermVectors => File.Exists(_basePath + ".tvd") && File.Exists(_basePath + ".tvx");

    private TermVectorsReader? EnsureTermVectorsReader()
    {
        if (_termVectorsReader is not null) return _termVectorsReader;

        var tvdPath = _basePath + ".tvd";
        var tvxPath = _basePath + ".tvx";
        if (!File.Exists(tvdPath) || !File.Exists(tvxPath)) return null;

        var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            _termVectorsReader ??= TermVectorsReader.Open(tvdPath, tvxPath);
        }
        return _termVectorsReader;
    }

    /// <summary>
    /// Gets or creates a cached qualified term string (field\0term).
    /// </summary>
    private string GetQualifiedTerm(string field, string term)
    {
        return string.Concat(field, "\x00", term);
    }

    /// <summary>
    /// Returns document IDs matching the given field and term.
    /// </summary>
    public int[] GetDocIds(string field, string term)
    {
        var qualifiedTerm = GetQualifiedTerm(field, term);
        if (!TryGetCachedOffset(qualifiedTerm, out long offset))
            return [];

        return ReadPostingsAtOffset(offset);
    }

    /// <summary>
    /// Returns document IDs for a pre-built qualified term string.
    /// </summary>
    internal int[] GetDocIds(string qualifiedTerm)
    {
        if (!TryGetCachedOffset(qualifiedTerm, out long offset))
            return [];

        return ReadPostingsAtOffset(offset);
    }

    /// <summary>Returns the document frequency for a term (count only, no full decode).</summary>
    public int GetDocFreq(string field, string term)
    {
        var qualifiedTerm = GetQualifiedTerm(field, term);
        return GetDocFreqByQualified(qualifiedTerm);
    }

    /// <summary>Returns the document frequency for a pre-built qualified term string.</summary>
    internal int GetDocFreq(string qualifiedTerm)
    {
        return GetDocFreqByQualified(qualifiedTerm);
    }

    /// <summary>Returns the document frequency using a pre-built qualified term string.</summary>
    public int GetDocFreqByQualified(string qualifiedTerm)
    {
        if (!TryGetCachedOffset(qualifiedTerm, out long offset))
            return 0;

        long cursor = offset;
        return _posInput.ReadInt32(ref cursor);
    }

    /// <summary>Reads docFreq directly from a known postings file offset (no dictionary lookup).</summary>
    internal int ReadDocFreqAtOffset(long offset)
    {
        long cursor = offset;
        return _posInput.ReadInt32(ref cursor);
    }

    /// <summary>Thread-safe cache for recent term lookups.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetCachedOffset(string qualifiedTerm, out long offset)
    {
        if (_termOffsetCache.TryGet(qualifiedTerm, out var entry))
        {
            offset = entry.Offset;
            return entry.Found;
        }

        bool found = _dicReader.TryGetPostingsOffset(qualifiedTerm, out offset);
        _termOffsetCache.Set(qualifiedTerm, (offset, found));
        return found;
    }

    internal int TermOffsetCacheCount => _termOffsetCache.Count;

    internal long TermOffsetCacheHits => _termOffsetCache.Hits;

    /// <inheritdoc/>
    public void Dispose()
    {
        _posInput.Dispose();
        _dicReader.Dispose();
        _storedReader?.Dispose();
        foreach (var r in _vectorReaders.Values) r.Dispose();
        _vectorReaders.Clear();
        _vectorPaths.Clear();
        _termVectorsReader?.Dispose();
        _bkdReader?.Dispose();
    }

    private sealed class TermOffsetCache
    {
        private readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<(string Key, (long Offset, bool Found) Value)>> _entries;
        private readonly LinkedList<(string Key, (long Offset, bool Found) Value)> _lru = new();
        private readonly Lock _lock = new();
        private long _hits;

        internal TermOffsetCache(int capacity)
        {
            _capacity = capacity;
            _entries = new Dictionary<string, LinkedListNode<(string, (long, bool))>>(capacity, StringComparer.Ordinal);
        }

        internal int Count
        {
            get
            {
                lock (_lock)
                {
                    return _entries.Count;
                }
            }
        }

        internal long Hits => Volatile.Read(ref _hits);

        internal bool TryGet(string key, out (long Offset, bool Found) value)
        {
            lock (_lock)
            {
                if (_entries.TryGetValue(key, out var node))
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    Interlocked.Increment(ref _hits);
                    value = node.Value.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        internal void Set(string key, (long Offset, bool Found) value)
        {
            lock (_lock)
            {
                if (_entries.TryGetValue(key, out var existing))
                {
                    existing.Value = (key, value);
                    _lru.Remove(existing);
                    _lru.AddFirst(existing);
                    return;
                }

                var node = new LinkedListNode<(string, (long, bool))>((key, value));
                _lru.AddFirst(node);
                _entries[key] = node;

                if (_entries.Count <= _capacity)
                    return;

                var last = _lru.Last;
                if (last is null)
                    return;

                _lru.RemoveLast();
                _entries.Remove(last.Value.Key);
            }
        }
    }

    private static void ValidateSegmentFiles(string basePath, int docCount)
    {
        ValidateExistingFile(basePath + ".seg");
        ValidateExistingFile(basePath + ".dic");
        ValidateExistingFile(basePath + ".pos");
        ValidateExistingFile(basePath + ".nrm");

        var segLength = new FileInfo(basePath + ".seg").Length;
        if (segLength == 0)
            throw new InvalidDataException($"Segment metadata file is empty or truncated: '{basePath}.seg'.");

        var dicLength = new FileInfo(basePath + ".dic").Length;
        if (dicLength < sizeof(int))
            throw new InvalidDataException($"Segment dictionary file is truncated: '{basePath}.dic'.");

        var nrmLength = new FileInfo(basePath + ".nrm").Length;
        // Per-field format: 4-byte field count header = minimum 4 bytes
        if (nrmLength < 4)
            throw new InvalidDataException(
                $"Segment norms file '{basePath}.nrm' is truncated: expected at least 4 bytes, found {nrmLength}.");
    }

    private static void ValidateExistingFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"Segment file is missing: '{path}'.", path);
    }
}
