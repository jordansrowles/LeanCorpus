using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Threading;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Reads a single immutable segment from disc via MMapDirectory.
/// </summary>
internal sealed partial class SegmentReaderState : IDisposable
{
    private readonly MMapDirectory _directory;
    private readonly SegmentInfo _info;
    private TermDictionaryReader? _dictionaryReader;
    private IndexInput? _postingsInput;
    private StoredFieldsReader? _storedReader;
    private bool _storedReaderLoaded;
    private NormState? _normState;
    private readonly Dictionary<string, string> _vectorPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VectorReader> _vectorReaders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QuantisedVectorReader> _quantisedVectorReaders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VectorQuantisation> _vectorQuantisation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HnswGraph?> _hnswGraphs = new(StringComparer.Ordinal);
    private readonly object _hnswLoadLock = new();
    private LiveDocs? _liveDocuments;
    private bool _liveDocumentsLoaded;

    private const int MaxTermOffsetCacheSize = 1024;
    private readonly TermOffsetCache _termOffsetCache = new(MaxTermOffsetCacheSize);

    private static readonly QualifiedTermCache s_qualifiedTermCache = new();

    // Lazy-loaded Stage 2 features (thread-safe via LazyInitializer)
    private Dictionary<string, Dictionary<int, double>>? _numericIndex;
    private Dictionary<string, Dictionary<int, long>>? _int64Index;
    private Dictionary<string, double[]>? _numericDocValues;
    private Dictionary<string, Util.RoaringBitmap?>? _numericDocValuesPresence;
    private Dictionary<string, long[]>? _int64DocValues;
    private Dictionary<string, Util.RoaringBitmap?>? _int64DocValuesPresence;
    private Dictionary<string, string[]>? _sortedDocValues;
    private Dictionary<string, Util.RoaringBitmap?>? _sortedDocValuesPresence;
    private Dictionary<string, string[][]>? _sortedSetDocValues;
    private Dictionary<string, double[][]>? _sortedNumericDocValues;
    private Dictionary<string, long[][]>? _int64SortedDocValues;
    private Dictionary<string, byte[][][]>? _binaryDocValues;
    private TermVectorsReader? _termVectorsReader;
    private Codecs.Bkd.BKDReader? _bkdReader;
    private bool _bkdReaderLoaded;
    private Codecs.Bkd.Int64BKDReader? _int64BkdReader;
    private bool _int64BkdReaderLoaded;
    private object? _lazyInitLock;
    private readonly string _basePath;
    private ParentBitSet? _parentBitSet;
    private bool _parentBitSetLoaded;

    /// <summary>Gets or sets the document base offset for this reader within the global document namespace.</summary>
    public int DocBase { get; set; }

    /// <summary>Gets the segment metadata for this reader.</summary>
    public SegmentInfo Info => _info;

    /// <summary>Gets the directory this reader was opened from.</summary>
    internal MMapDirectory Directory => _directory;

    /// <summary>Gets the total number of documents in this segment, including deleted documents.</summary>
    public int MaxDoc => _info.DocCount;

    /// <summary>
    /// Initialises a new <see cref="SegmentReaderState"/> for the given segment.
    /// Records component paths; each component is opened and validated on first use.
    /// </summary>
    /// <param name="directory">The directory containing the segment files.</param>
    /// <param name="info">The segment metadata.</param>
    /// <exception cref="FileNotFoundException">Thrown if required segment files are missing.</exception>
    /// <exception cref="InvalidDataException">Thrown if segment files contain corrupted or incompatible data.</exception>
    internal SegmentReaderState(MMapDirectory directory, SegmentInfo info)
    {
        _directory = directory;
        _info = info;
        _basePath = Path.Combine(directory.DirectoryPath, info.SegmentId);

        // Vector fields: record paths only. Opening the mmap-backed readers is deferred
        // until a VectorQuery or explicit vector read actually needs them.
        if (info.VectorFields.Count > 0)
        {
            foreach (var vf in info.VectorFields)
            {
                if (vf.Quantisation != VectorQuantisation.None)
                {
                    var vqPath = VectorFilePaths.QuantisedVectorFile(_basePath, vf.FieldName);
                    if (FileOpenRetry.FileExists(vqPath))
                    {
                        _vectorPaths[vf.FieldName] = vqPath;
                        _vectorQuantisation[vf.FieldName] = vf.Quantisation;
                    }
                }
                else
                {
                    var perFieldVecPath = VectorFilePaths.VectorFile(_basePath, vf.FieldName);
                    if (FileOpenRetry.FileExists(perFieldVecPath))
                        _vectorPaths[vf.FieldName] = perFieldVecPath;
                }
            }
        }
        else
        {
            // Legacy single-vector segment: pre-multi-vector layout.
            var legacyVecPath = _basePath + ".vec";
            if (FileOpenRetry.FileExists(legacyVecPath))
                _vectorPaths[string.Empty] = legacyVecPath;
        }

        // DocValues and numeric indexes remain genuinely on demand. The searcher's
        // snapshot lease now protects their files from concurrent merge cleanup.
    }

    private TermDictionaryReader DictionaryReader
    {
        get
        {
            if (_dictionaryReader is not null) return _dictionaryReader;
            var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
            lock (lockObj)
                return _dictionaryReader ??= TermDictionaryReader.Open(_basePath + ".dic");
        }
    }

    private IndexInput PostingsInput
    {
        get
        {
            if (_postingsInput is not null) return _postingsInput;
            var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
            lock (lockObj)
            {
                if (_postingsInput is not null) return _postingsInput;
                var input = _directory.OpenInput(_info.SegmentId + ".pos");
                try
                {
                    _ = Codecs.Postings.PostingsEnum.ValidateFileHeader(input);
                    _postingsInput = input;
                    return input;
                }
                catch
                {
                    input.Dispose();
                    throw;
                }
            }
        }
    }

    private StoredFieldsReader? StoredReader
    {
        get
        {
            if (Volatile.Read(ref _storedReaderLoaded)) return _storedReader;
            var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
            lock (lockObj)
            {
                if (_storedReaderLoaded) return _storedReader;
                var fdtPath = _basePath + ".fdt";
                var fdxPath = _basePath + ".fdx";
                if (FileOpenRetry.FileExists(fdtPath) && FileOpenRetry.FileExists(fdxPath))
                    _storedReader = StoredFieldsReader.Open(fdtPath, fdxPath);
                Volatile.Write(ref _storedReaderLoaded, true);
                return _storedReader;
            }
        }
    }

    private LiveDocs? LiveDocuments
    {
        get
        {
            if (Volatile.Read(ref _liveDocumentsLoaded)) return _liveDocuments;
            var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
            lock (lockObj)
            {
                if (_liveDocumentsLoaded) return _liveDocuments;
                var delPath = _info.DelGeneration.HasValue
                    ? _basePath + $"_gen_{_info.DelGeneration.Value}.del"
                    : _basePath + ".del";
                if (FileOpenRetry.FileExists(delPath))
                    _liveDocuments = LiveDocs.Deserialise(delPath, _info.DocCount);
                Volatile.Write(ref _liveDocumentsLoaded, true);
                return _liveDocuments;
            }
        }
    }

    private NormState Norms
    {
        get
        {
            if (_normState is not null) return _normState;
            var lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
            lock (lockObj)
            {
                if (_normState is not null) return _normState;
                var normsData = NormsReader.Read(_basePath + ".nrm");
                var norms = normsData.Norms.ToFrozenDictionary(StringComparer.Ordinal);
                var boosts = normsData.Boosts.ToFrozenDictionary(StringComparer.Ordinal);
                var exactLengths = FieldLengthReader.TryRead(_basePath + ".fln");
                FrozenDictionary<string, int[]> lengths;
                if (exactLengths is not null)
                {
                    lengths = exactLengths.ToFrozenDictionary(StringComparer.Ordinal);
                }
                else
                {
                    var calculated = new Dictionary<string, int[]>(norms.Count, StringComparer.Ordinal);
                    foreach (var (fieldName, values) in norms)
                    {
                        var fieldLengths = new int[values.Length];
                        for (int i = 0; i < values.Length; i++)
                        {
                            float n = values[i] / 255f;
                            fieldLengths[i] = n <= 0f ? 1 : Math.Max(1, (int)MathF.Round(1.0f / n - 1.0f));
                        }
                        calculated[fieldName] = fieldLengths;
                    }
                    lengths = calculated.ToFrozenDictionary(StringComparer.Ordinal);
                }
                return _normState = new NormState(norms, boosts, lengths);
            }
        }
    }

    private FrozenDictionary<string, byte[]> FieldNorms => Norms.Norms;
    private FrozenDictionary<string, float[]> FieldBoosts => Norms.Boosts;
    private FrozenDictionary<string, int[]> FieldLengthsPerField => Norms.FieldLengths;

    /// <summary>
    /// Returns <see langword="true"/> if the document with the given ID has not been deleted.
    /// </summary>
    /// <param name="docId">The local (segment-relative) document ID to check.</param>
    /// <returns><see langword="true"/> if the document is live; <see langword="false"/> if deleted.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsLive(int docId) => LiveDocuments?.IsLive(docId) ?? true;

    /// <summary>
    /// Returns <see langword="true"/> if the document is soft-deleted (has a recorded
    /// soft-delete timestamp). Sets <paramref name="timestamp"/> to the Unix-millisecond
    /// timestamp when the document was soft-deleted.
    /// </summary>
    public bool IsSoftDeleted(int docId, out long timestamp)
    {
        if (LiveDocuments is null || LiveDocuments.IsLive(docId))
        {
            timestamp = 0;
            return false;
        }

        var timestamps = LiveDocuments.SoftDeleteTimestamps;
        if (timestamps is not null && timestamps.TryGetValue(docId, out timestamp))
            return true;

        timestamp = 0;
        return false;
    }

    /// <summary>True when this segment has no deleted documents, allowing callers to skip per-doc IsLive checks.</summary>
    public bool HasDeletions => LiveDocuments is not null;

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
            if (FileOpenRetry.FileExists(pbsPath))
                _parentBitSet = ParentBitSet.ReadFrom(pbsPath);
            Volatile.Write(ref _parentBitSetLoaded, true);
        }
        return _parentBitSet;
    }

    /// <summary>Returns the quantised norm value for a document in a specific field (0..1 range).</summary>
    public float GetNorm(int docId, string field)
    {
        if (FieldNorms.TryGetValue(field, out var norms) && (uint)docId < (uint)norms.Length)
            return norms[docId] / 255f;
        return 0f;
    }

    /// <summary>Returns the index-time field boost for a document in a specific field.</summary>
    public float GetFieldBoost(int docId, string field)
    {
        if (FieldBoosts.TryGetValue(field, out var boosts) && (uint)docId < (uint)boosts.Length)
            return boosts[docId];
        return 1.0f;
    }

    /// <summary>Resolves the sparse boost array for a field when it has non-default boosts.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetFieldBoosts(string field, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out float[]? boosts)
    {
        return FieldBoosts.TryGetValue(field, out boosts);
    }

    /// <summary>
    /// Returns an approximate field length for BM25 for a specific field, derived from the stored norm.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetFieldLength(int docId, string field)
    {
        if (FieldLengthsPerField.TryGetValue(field, out var fieldLengths))
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
        return FieldLengthsPerField.TryGetValue(field, out lengths);
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
    public bool HasTermVectors => FileOpenRetry.FileExists(_basePath + ".tvd") && FileOpenRetry.FileExists(_basePath + ".tvx");

    private TermVectorsReader? EnsureTermVectorsReader()
    {
        if (_termVectorsReader is not null) return _termVectorsReader;

        var tvdPath = _basePath + ".tvd";
        var tvxPath = _basePath + ".tvx";
        if (!FileOpenRetry.FileExists(tvdPath) || !FileOpenRetry.FileExists(tvxPath)) return null;

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
        int length = QualifiedTermHelpers.QualifiedTermLength(field, term);
        Span<char> buffer = length <= 256
            ? stackalloc char[length]
            : new char[length];
        ReadOnlySpan<char> qt = QualifiedTermHelpers.BuildQualifiedTerm(field, term, buffer);
        return s_qualifiedTermCache.GetOrAdd(qt);
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

        PostingsEnum.ReadTermMetadata(PostingsInput, offset, out _, out int docFreq,
            out _, out _, out _, out _);
        return docFreq;
    }

    /// <summary>
    /// Returns the document frequency for a qualified term span without allocating
    /// a string. Bypasses the per-segment LRU cache and goes directly to the FST.
    /// Suitable for one-shot lookups (e.g. MLT term extraction) where the term
    /// is looked up exactly once.
    /// </summary>
    internal int GetDocFreqByQualified(ReadOnlySpan<char> qualifiedTerm)
    {
        if (!DictionaryReader.TryGetPostingsOffset(qualifiedTerm, out long offset))
            return 0;

        PostingsEnum.ReadTermMetadata(PostingsInput, offset, out _, out int docFreq,
            out _, out _, out _, out _);
        return docFreq;
    }
    /// <summary>
    /// Sums all per-document term frequencies for the given qualified term.
    /// Iterates the full postings list; call only when collection-level term statistics
    /// are required (language-model similarities).
    /// </summary>
    internal long GetCollectionFrequency(string qualifiedTerm)
    {
        using var postings = GetPostingsEnum(qualifiedTerm);
        long sum = 0;
        while (postings.MoveNext())
            sum += postings.Freq;
        return sum;
    }

    /// <summary>Reads docFreq directly from a known postings file offset (no dictionary lookup).</summary>
    internal int ReadDocFreqAtOffset(long offset)
    {
        PostingsEnum.ReadTermMetadata(PostingsInput, offset, out _, out int docFreq,
            out _, out _, out _, out _);
        return docFreq;
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

        bool found = DictionaryReader.TryGetPostingsOffset(qualifiedTerm, out offset);
        _termOffsetCache.Set(qualifiedTerm, (offset, found));
        return found;
    }

    internal int TermOffsetCacheCount => _termOffsetCache.Count;

    internal long TermOffsetCacheHits => _termOffsetCache.Hits;

    /// <inheritdoc/>
    public void Dispose()
    {
        _postingsInput?.Dispose();
        _dictionaryReader?.Dispose();
        _storedReader?.Dispose();
        foreach (var r in _vectorReaders.Values) r.Dispose();
        _vectorReaders.Clear();
        _vectorPaths.Clear();
        foreach (var r in _quantisedVectorReaders.Values) r.Dispose();
        _quantisedVectorReaders.Clear();
        _termVectorsReader?.Dispose();
        _bkdReader?.Dispose();
        _int64BkdReader?.Dispose();
    }

    private sealed record NormState(
        FrozenDictionary<string, byte[]> Norms,
        FrozenDictionary<string, float[]> Boosts,
        FrozenDictionary<string, int[]> FieldLengths);

    private sealed class TermOffsetCache
    {
        private const int ShardCount = 16;
        private readonly TermOffsetShard[] _shards;

        internal TermOffsetCache(int capacity)
        {
            int perShard = Math.Max(16, capacity / ShardCount);
            _shards = new TermOffsetShard[ShardCount];
            for (int i = 0; i < ShardCount; i++)
                _shards[i] = new TermOffsetShard(perShard);
        }

        private static int ShardIndex(string key) => (int)((uint)key.GetHashCode() % ShardCount);

        internal int Count
        {
            get
            {
                int total = 0;
                foreach (var shard in _shards)
                    total += shard.Count;
                return total;
            }
        }

        internal long Hits
        {
            get
            {
                long total = 0;
                foreach (var shard in _shards)
                    total += Volatile.Read(ref shard._hits);
                return total;
            }
        }

        internal bool TryGet(string key, out (long Offset, bool Found) value)
            => _shards[ShardIndex(key)].TryGet(key, out value);

        internal void Set(string key, (long Offset, bool Found) value)
            => _shards[ShardIndex(key)].Set(key, value);
    }

    private sealed class TermOffsetShard
    {
        internal readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<(string Key, (long Offset, bool Found) Value)>> _entries;
        private readonly LinkedList<(string Key, (long Offset, bool Found) Value)> _lru = new();
        private readonly Lock _lock = new();
        internal long _hits;

        internal TermOffsetShard(int capacity)
        {
            _capacity = capacity;
            _entries = new Dictionary<string, LinkedListNode<(string, (long, bool))>>(capacity, StringComparer.Ordinal);
        }

        internal int Count { get { lock (_lock) return _entries.Count; } }

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

                if (_entries.Count <= _capacity) return;

                var last = _lru.Last!;
                _lru.RemoveLast();
                _entries.Remove(last.Value.Key);
            }
        }
    }

}
