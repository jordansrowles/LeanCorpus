using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Store;
using System.Text.RegularExpressions;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Metadata-first facade for one immutable segment. Heavy codec state is loaded on
/// first use and retained privately or through a searcher's bounded reader cache.
/// </summary>
public sealed partial class SegmentReader : IDisposable
{
    [ThreadStatic] private static QueryPinContext? t_queryPins;

    private readonly struct QueryPinFrame
    {
        internal SegmentReader Reader { get; }
        internal SegmentReaderState State { get; }
        internal long Token { get; }
        internal bool OwnsResources { get; }
        internal BoundedLruCache<string, SegmentReaderState>.Lease CacheLease { get; }
        internal LifetimeLease OperationLease { get; }
        internal LifetimeLease DirectoryLease { get; }

        internal QueryPinFrame(
            SegmentReader reader,
            SegmentReaderState state,
            long token,
            BoundedLruCache<string, SegmentReaderState>.Lease cacheLease,
            LifetimeLease operationLease,
            LifetimeLease directoryLease,
            bool ownsResources)
        {
            Reader = reader;
            State = state;
            Token = token;
            OwnsResources = ownsResources;
            CacheLease = cacheLease;
            OperationLease = operationLease;
            DirectoryLease = directoryLease;
        }
    }

    internal sealed class QueryPinContext
    {
        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
        private readonly List<QueryPinFrame> _frames = new(capacity: 4);
        private long _nextToken;

        internal bool TryGetFastState(
            SegmentReader reader,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SegmentReaderState? state)
        {
            for (int i = _frames.Count - 1; i >= 0; i--)
            {
                QueryPinFrame frame = _frames[i];
                if (ReferenceEquals(frame.Reader, reader))
                {
                    state = frame.State;
                    return true;
                }
            }

            state = null;
            return false;
        }

        internal long Push(
            SegmentReader reader,
            SegmentReaderState state,
            BoundedLruCache<string, SegmentReaderState>.Lease cacheLease,
            LifetimeLease operationLease,
            LifetimeLease directoryLease,
            bool ownsResources)
        {
            long token = ++_nextToken;
            _frames.Add(new QueryPinFrame(
                reader, state, token, cacheLease, operationLease, directoryLease, ownsResources));
            return token;
        }

        internal void Release(long token)
        {
            if (_ownerThreadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("A segment query lease must be disposed on its acquiring thread.");

            int frameIndex = -1;
            for (int i = _frames.Count - 1; i >= 0; i--)
            {
                if (_frames[i].Token == token)
                {
                    frameIndex = i;
                    break;
                }
            }

            if (frameIndex < 0)
                return;
            if (frameIndex != _frames.Count - 1)
                throw new InvalidOperationException("Segment query leases must be disposed in reverse acquisition order.");

            QueryPinFrame frame = _frames[frameIndex];
            _frames.RemoveAt(frameIndex);
            if (!frame.OwnsResources)
                return;

            List<Exception>? failures = null;
            try { frame.CacheLease.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            try { frame.OperationLease.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            try { frame.DirectoryLease.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }

            if (failures is not null)
                throw new AggregateException("Segment query lease cleanup failed.", failures);
        }
    }

    private readonly MMapDirectory _directory;
    private readonly SegmentDescriptor _info;
    private readonly CodecCatalog _codecCatalog;
    private readonly BoundedLruCache<string, SegmentReaderState> _cache;
    private readonly Func<SegmentReaderState> _stateFactory;
    private readonly bool _ownsCache;
    private readonly OperationDrain _operations = new();
    private FileSnapshotLease? _snapshot;
    private bool _disposed;
    private int _disposeStarted;

    /// <summary>Gets or sets the document base offset for this reader within the global document namespace.</summary>
    public int DocBase { get; set; }

    /// <summary>Gets the segment metadata for this reader.</summary>
    public SegmentDescriptor Info => _info;

    /// <summary>Gets process-wide entry and eviction metrics for qualified-term interning.</summary>
    public static CacheMetricsSnapshot QualifiedTermCacheMetrics => SegmentReaderState.QualifiedTermCacheMetrics;

    /// <summary>Gets the directory this reader was opened from.</summary>
    internal MMapDirectory Directory => _directory;

    /// <summary>Gets the total number of documents in this segment, including deleted documents.</summary>
    public int MaxDoc => _info.DocCount;

    /// <summary>Creates a lazy reader that privately retains its heavy state.</summary>
    public SegmentReader(MMapDirectory directory, SegmentInfo info)
        : this(directory, info, CaptureDefaultCodecCatalog(directory, info))
    {
    }

    /// <summary>Creates a lazy reader that uses the supplied immutable codec catalogue.</summary>
    public SegmentReader(MMapDirectory directory, SegmentInfo info, CodecCatalog codecCatalog)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(codecCatalog);
        CompressionCodecRegistry.MarkIndexOpened();
        var descriptor = new SegmentDescriptor(info);
        var segmentId = descriptor.SegmentId;
        var snapshot = directory.AcquireSnapshot(
            name => IsSegmentFile(segmentId, name), out _);
        try
        {
            ValidateRequiredFiles(directory, descriptor);
            _snapshot = snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
        _directory = directory;
        _info = descriptor;
        _codecCatalog = codecCatalog;
        _cache = new BoundedLruCache<string, SegmentReaderState>(1, StringComparer.Ordinal);
        _stateFactory = () => new SegmentReaderState(directory, descriptor, _codecCatalog);
        _ownsCache = true;
    }

    internal SegmentReader(
        MMapDirectory directory,
        SegmentInfo info,
        BoundedLruCache<string, SegmentReaderState> cache,
        CodecCatalog? codecCatalog = null)
        : this(directory, new SegmentDescriptor(info), cache, codecCatalog)
    {
    }

    internal SegmentReader(
        MMapDirectory directory,
        SegmentDescriptor info,
        BoundedLruCache<string, SegmentReaderState> cache,
        CodecCatalog? codecCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(info);
        if (codecCatalog is null)
            codecCatalog = CaptureDefaultCodecCatalog(directory, info);
        else
            CompressionCodecRegistry.MarkIndexOpened();
        ValidateRequiredFiles(directory, info);
        _directory = directory;
        _info = info;
        _codecCatalog = codecCatalog;
        _cache = cache;
        _stateFactory = () => new SegmentReaderState(directory, info, _codecCatalog);
    }

    private static CodecCatalog CaptureDefaultCodecCatalog(MMapDirectory directory, object segmentInfo)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(segmentInfo);
        CompressionCodecRegistry.MarkIndexOpened();
        return CodecCatalog.Default;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal SegmentReaderLease AcquireReadLease()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (TryGetFastState(out var pinnedState))
            return new SegmentReaderLease(pinnedState);

        var operationLease = _operations.Acquire(this);
        LifetimeLease directoryLease = default;
        try
        {
            directoryLease = _directory.AcquireOperationLease();
            return new SegmentReaderLease(
                _cache.Acquire(_info.SegmentId, _stateFactory), operationLease, directoryLease);
        }
        catch
        {
            directoryLease.Dispose();
            operationLease.Dispose();
            throw;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private bool TryGetFastState([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SegmentReaderState? state)
    {
        var queryPins = t_queryPins;
        if (queryPins is not null && queryPins.TryGetFastState(this, out state))
            return true;
        state = null;
        return false;
    }

    internal SegmentQueryLease AcquireQueryLease()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var queryPins = t_queryPins ??= new QueryPinContext();
        if (TryGetFastState(out var pinnedState))
        {
            long nestedToken = queryPins.Push(this, pinnedState, default, default, default, ownsResources: false);
            return new SegmentQueryLease(queryPins, nestedToken);
        }

        var operationLease = _operations.Acquire(this);
        LifetimeLease directoryLease = default;
        BoundedLruCache<string, SegmentReaderState>.Lease cacheLease = default;
        try
        {
            directoryLease = _directory.AcquireOperationLease();
            cacheLease = _cache.Acquire(_info.SegmentId, _stateFactory);
            long token = queryPins.Push(
                this, cacheLease.Value, cacheLease, operationLease, directoryLease, ownsResources: true);
            return new SegmentQueryLease(queryPins, token);
        }
        catch
        {
            cacheLease.Dispose();
            directoryLease.Dispose();
            operationLease.Dispose();
            throw;
        }
    }

    internal static string[] SelectSegmentFiles(string segmentId, IReadOnlyCollection<string> inventory)
    {
        var selected = new HashSet<string>(
            SegmentFileSet.FromFileNames(segmentId, inventory, includeTemporary: false).FileNames,
            StringComparer.Ordinal);
        return inventory.Where(selected.Contains).ToArray();
    }

    // Temporary codec files are publication machinery, not immutable snapshot files.
    internal static bool IsSegmentFile(string segmentId, string name)
        => SegmentFileSet.IsSnapshotFile(segmentId, name);

    private static void ValidateRequiredFiles(MMapDirectory directory, SegmentDescriptor info)
    {
        using var fileAccess = SegmentFileAccess.Open(directory, info);
        SegmentStructureValidator.ValidateRequiredFiles(info, fileAccess.LogicalFiles);
    }

    public bool IsLive(int docId) { if (TryGetFastState(out var state)) return state.IsLive(docId); using var lease = AcquireReadLease(); return lease.State.IsLive(docId); }
    public bool IsSoftDeleted(int docId, out long timestamp) { using var lease = AcquireReadLease(); return lease.State.IsSoftDeleted(docId, out timestamp); }
    public bool HasDeletions { get { if (TryGetFastState(out var state)) return state.HasDeletions; using var lease = AcquireReadLease(); return lease.State.HasDeletions; } }
    internal ParentBitSet? GetParentBitSet() { using var lease = AcquireReadLease(); return lease.State.GetParentBitSet(); }
    internal bool FileExists(string extension) { using var lease = AcquireReadLease(); return lease.State.FileExists(extension); }
    internal IndexInput OpenInput(string extension) { using var lease = AcquireReadLease(); return lease.State.OpenInput(extension); }
    public float GetNorm(int docId, string field) { if (TryGetFastState(out var state)) return state.GetNorm(docId, field); using var lease = AcquireReadLease(); return lease.State.GetNorm(docId, field); }
    public float GetFieldBoost(int docId, string field) { if (TryGetFastState(out var state)) return state.GetFieldBoost(docId, field); using var lease = AcquireReadLease(); return lease.State.GetFieldBoost(docId, field); }
    internal bool TryGetFieldBoosts(string field, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out float[]? boosts) { if (TryGetFastState(out var state)) return state.TryGetFieldBoosts(field, out boosts); using var lease = AcquireReadLease(); return lease.State.TryGetFieldBoosts(field, out boosts); }
    public int GetFieldLength(int docId, string field) { if (TryGetFastState(out var state)) return state.GetFieldLength(docId, field); using var lease = AcquireReadLease(); return lease.State.GetFieldLength(docId, field); }
    /// <summary>
    /// Tries to retrieve field lengths for this segment. The returned array is a
    /// defensive copy and can be changed without affecting later reads.
    /// </summary>
    public bool TryGetFieldLengths(string field, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out int[]? lengths)
    {
        if (TryGetFastState(out var fastState))
        {
            if (fastState.TryGetFieldLengths(field, out var fastLengths))
            {
                lengths = (int[])fastLengths.Clone();
                return true;
            }

            lengths = null;
            return false;
        }

        using var lease = AcquireReadLease();
        if (lease.State.TryGetFieldLengths(field, out var stateLengths))
        {
            lengths = (int[])stateLengths.Clone();
            return true;
        }

        lengths = null;
        return false;
    }

    /// <summary>Gets a read-only field-length view while the current query lease retains this segment state.</summary>
    internal ReadOnlyMemory<int>? GetFieldLengthsForQuery(string field)
    {
        if (!TryGetFastState(out var state))
            throw new InvalidOperationException("Field-length views require an active segment query lease.");

        return state.TryGetFieldLengths(field, out var lengths)
            ? new ReadOnlyMemory<int>(lengths)
            : null;
    }
    public Dictionary<string, List<TermVectorEntry>>? GetTermVectors(int docId) { using var lease = AcquireReadLease(); return lease.State.GetTermVectors(docId); }
    public bool HasTermVectors { get { using var lease = AcquireReadLease(); return lease.State.HasTermVectors; } }
    public int[] GetDocIds(string field, string term) { using var lease = AcquireReadLease(); return lease.State.GetDocIds(field, term); }
    internal int[] GetDocIds(string qualifiedTerm) { using var lease = AcquireReadLease(); return lease.State.GetDocIds(qualifiedTerm); }
    public int GetDocFreq(string field, string term) { using var lease = AcquireReadLease(); return lease.State.GetDocFreq(field, term); }
    internal int GetDocFreq(string qualifiedTerm) { using var lease = AcquireReadLease(); return lease.State.GetDocFreq(qualifiedTerm); }
    public int GetDocFreqByQualified(string qualifiedTerm) { if (TryGetFastState(out var state)) return state.GetDocFreqByQualified(qualifiedTerm); using var lease = AcquireReadLease(); return lease.State.GetDocFreqByQualified(qualifiedTerm); }
    internal int GetDocFreqByQualified(ReadOnlySpan<char> qualifiedTerm) { using var lease = AcquireReadLease(); return lease.State.GetDocFreqByQualified(qualifiedTerm); }
    internal long GetCollectionFrequency(string qualifiedTerm) { using var lease = AcquireReadLease(); return lease.State.GetCollectionFrequency(qualifiedTerm); }
    internal int ReadDocFreqAtOffset(long offset) { using var lease = AcquireReadLease(); return lease.State.ReadDocFreqAtOffset(offset); }
    internal int TermOffsetCacheCount { get { using var lease = AcquireReadLease(); return lease.State.TermOffsetCacheCount; } }
    internal long TermOffsetCacheHits { get { using var lease = AcquireReadLease(); return lease.State.TermOffsetCacheHits; } }

    public PostingsEnum GetPostingsEnum(string qualifiedTerm)
    {
        if (TryGetFastState(out var state)) return state.GetPostingsEnum(qualifiedTerm);
        return RetainPostingsLease(AcquireReadLease(), static (current, arg) => current.GetPostingsEnum(arg), qualifiedTerm);
    }
    internal PostingsEnum GetPostingsEnum(byte[] qualifiedTerm)
    {
        if (TryGetFastState(out var state)) return state.GetPostingsEnum(qualifiedTerm);
        return RetainPostingsLease(
            AcquireReadLease(),
            static (current, arg) => current.GetPostingsEnum(arg),
            qualifiedTerm);
    }
    public PostingsEnum GetPostingsEnumAtOffset(long offset)
    {
        if (TryGetFastState(out var state)) return state.GetPostingsEnumAtOffset(offset);
        return RetainPostingsLease(AcquireReadLease(), static (current, arg) => current.GetPostingsEnumAtOffset(arg), offset);
    }
    public PostingsEnum GetPostingsEnumWithPositions(string qualifiedTerm)
    {
        if (TryGetFastState(out var state)) return state.GetPostingsEnumWithPositions(qualifiedTerm);
        return RetainPostingsLease(AcquireReadLease(), static (current, arg) => current.GetPostingsEnumWithPositions(arg), qualifiedTerm);
    }

    private static PostingsEnum RetainPostingsLease<T>(
        SegmentReaderLease lease,
        Func<SegmentReaderState, T, PostingsEnum> factory,
        T argument)
    {
        try
        {
            var postings = factory(lease.State, argument);
            if (postings.DocFreq == 0)
            {
                lease.Dispose();
                return postings;
            }
            postings.AttachLifetimeLease(lease.DetachStateLifetimeLease());
            lease.Dispose();
            return postings;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public int[]? GetPositions(string field, string term, int docId) { using var lease = AcquireReadLease(); return lease.State.GetPositions(field, term, docId); }
    internal ReadOnlySpan<int> GetPositions(string qualifiedTerm, int docId) { using var lease = AcquireReadLease(); return lease.State.GetPositions(qualifiedTerm, docId); }
    internal int[]? GetPositionsArray(string qualifiedTerm, int docId) { using var lease = AcquireReadLease(); return lease.State.GetPositionsArray(qualifiedTerm, docId); }
    public int GetTermFrequency(string field, string term, int docId) { using var lease = AcquireReadLease(); return lease.State.GetTermFrequency(field, term, docId); }
    internal int GetTermFrequency(string qualifiedTerm, int docId) { using var lease = AcquireReadLease(); return lease.State.GetTermFrequency(qualifiedTerm, docId); }

    public List<(string Term, long Offset)> IntersectAutomaton(string fieldPrefix, IAutomaton automaton) { using var lease = AcquireReadLease(); return lease.State.IntersectAutomaton(fieldPrefix, automaton); }
    public List<(string Term, long Offset)> GetTermsWithPrefix(string qualifiedPrefix) { using var lease = AcquireReadLease(); return lease.State.GetTermsWithPrefix(qualifiedPrefix); }
    internal List<long> GetTermOffsetsWithPrefix(string qualifiedPrefix) { using var lease = AcquireReadLease(); return lease.State.GetTermOffsetsWithPrefix(qualifiedPrefix); }
    public List<(string Term, long Offset)> GetTermsMatching(string fieldPrefix, ReadOnlySpan<char> pattern) { using var lease = AcquireReadLease(); return lease.State.GetTermsMatching(fieldPrefix, pattern); }
    internal List<long> GetTermOffsetsMatching(string fieldPrefix, ReadOnlySpan<char> pattern) { using var lease = AcquireReadLease(); return lease.State.GetTermOffsetsMatching(fieldPrefix, pattern); }
    internal List<long> GetTermOffsetsMatchingWithPrefix(string fieldPrefix, ReadOnlySpan<char> literalPrefix, ReadOnlySpan<char> pattern) { using var lease = AcquireReadLease(); return lease.State.GetTermOffsetsMatchingWithPrefix(fieldPrefix, literalPrefix, pattern); }
    internal List<(string Term, long Offset)> GetTermsMatchingWithPrefix(string fieldPrefix, ReadOnlySpan<char> literalPrefix, ReadOnlySpan<char> pattern) { using var lease = AcquireReadLease(); return lease.State.GetTermsMatchingWithPrefix(fieldPrefix, literalPrefix, pattern); }
    public List<(string Term, long Offset)> GetAllTermsForField(string fieldPrefix) { using var lease = AcquireReadLease(); return lease.State.GetAllTermsForField(fieldPrefix); }
    public List<(string Term, long Offset, int Distance)> GetFuzzyMatches(string fieldPrefix, ReadOnlySpan<char> queryTerm, int maxEdits, int maxExpansions = 64) { using var lease = AcquireReadLease(); return lease.State.GetFuzzyMatches(fieldPrefix, queryTerm, maxEdits, maxExpansions); }
    public List<(string Term, long Offset)> GetTermsInRange(string fieldPrefix, string? lowerTerm, string? upperTerm, bool includeLower, bool includeUpper) { using var lease = AcquireReadLease(); return lease.State.GetTermsInRange(fieldPrefix, lowerTerm, upperTerm, includeLower, includeUpper); }
    public List<(string Term, long Offset)> GetTermsMatchingRegex(string fieldPrefix, Regex regex) { using var lease = AcquireReadLease(); return lease.State.GetTermsMatchingRegex(fieldPrefix, regex); }
    internal List<long> GetTermOffsetsContaining(string fieldPrefix, ReadOnlySpan<char> literal) { using var lease = AcquireReadLease(); return lease.State.GetTermOffsetsContaining(fieldPrefix, literal); }

    internal IReadOnlyDictionary<string, IReadOnlyList<StoredFieldValue>> GetStoredFieldValues(int docId) { using var lease = AcquireReadLease(); return lease.State.GetStoredFieldValues(docId); }
    internal IReadOnlyDictionary<string, IReadOnlyList<StoredFieldValue>> GetStoredFieldValues(int docId, ISet<string>? fieldsToLoad) { using var lease = AcquireReadLease(); return lease.State.GetStoredFieldValues(docId, fieldsToLoad); }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetStoredFields(int docId) { using var lease = AcquireReadLease(); return lease.State.GetStoredFields(docId); }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetStoredFields(int docId, ISet<string>? fieldsToLoad) { using var lease = AcquireReadLease(); return lease.State.GetStoredFields(docId, fieldsToLoad); }
    public IReadOnlyDictionary<string, IReadOnlyList<byte[]>> GetStoredBinaryFields(int docId) { using var lease = AcquireReadLease(); return lease.State.GetStoredBinaryFields(docId); }
    public IReadOnlyDictionary<string, IReadOnlyList<byte[]>> GetStoredBinaryFields(int docId, ISet<string>? fieldsToLoad) { using var lease = AcquireReadLease(); return lease.State.GetStoredBinaryFields(docId, fieldsToLoad); }

    public bool TryGetNumericValue(string field, int docId, out double value) { if (TryGetFastState(out var state)) return state.TryGetNumericValue(field, docId, out value); using var lease = AcquireReadLease(); return lease.State.TryGetNumericValue(field, docId, out value); }
    internal bool TryGetNumericDocValues(string field, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NumericDocValuesColumn? values) { if (TryGetFastState(out var state)) return state.TryGetNumericDocValues(field, out values); using var lease = AcquireReadLease(); return lease.State.TryGetNumericDocValues(field, out values); }
    internal bool TryGetNumericDocValuesPresence(string field, out Util.RoaringBitmap? presence)
    {
        if (TryGetFastState(out var state) && state.TryGetNumericDocValues(field, out var fastValues))
        {
            presence = fastValues.Presence;
            return true;
        }

        using var lease = AcquireReadLease();
        if (lease.State.TryGetNumericDocValues(field, out var values))
        {
            presence = values.Presence;
            return true;
        }

        presence = null;
        return false;
    }
    internal bool HasNumericDocValues(string field) { if (TryGetFastState(out var state)) return state.HasNumericDocValues(field); using var lease = AcquireReadLease(); return lease.State.HasNumericDocValues(field); }
    internal bool HasInt64DocValues(string field) { if (TryGetFastState(out var state)) return state.HasInt64DocValues(field); using var lease = AcquireReadLease(); return lease.State.HasInt64DocValues(field); }
    internal bool HasSortedDocValues(string field) { if (TryGetFastState(out var state)) return state.HasSortedDocValues(field); using var lease = AcquireReadLease(); return lease.State.HasSortedDocValues(field); }
    internal bool HasSortedSetDocValues(string field) { if (TryGetFastState(out var state)) return state.HasSortedSetDocValues(field); using var lease = AcquireReadLease(); return lease.State.HasSortedSetDocValues(field); }
    internal bool HasSortedNumericDocValues(string field) { if (TryGetFastState(out var state)) return state.HasSortedNumericDocValues(field); using var lease = AcquireReadLease(); return lease.State.HasSortedNumericDocValues(field); }
    internal bool HasSortedInt64DocValues(string field) { if (TryGetFastState(out var state)) return state.HasSortedInt64DocValues(field); using var lease = AcquireReadLease(); return lease.State.HasSortedInt64DocValues(field); }
    internal bool HasBinaryDocValues(string field) { if (TryGetFastState(out var state)) return state.HasBinaryDocValues(field); using var lease = AcquireReadLease(); return lease.State.HasBinaryDocValues(field); }
    internal void ValidateDocValuesDocumentCounts() { using var lease = AcquireReadLease(); lease.State.ValidateDocValuesDocumentCounts(); }
    public bool TryGetInt64Value(string field, int docId, out long value) { if (TryGetFastState(out var state)) return state.TryGetInt64Value(field, docId, out value); using var lease = AcquireReadLease(); return lease.State.TryGetInt64Value(field, docId, out value); }
    public bool TryGetSortedDocValue(string field, int docId, out string value) { if (TryGetFastState(out var state)) return state.TryGetSortedDocValue(field, docId, out value); using var lease = AcquireReadLease(); return lease.State.TryGetSortedDocValue(field, docId, out value); }
    public bool TryGetSortedDocOrdinal(string field, int docId, out int ordinal) { if (TryGetFastState(out var state)) return state.TryGetSortedDocOrdinal(field, docId, out ordinal); using var lease = AcquireReadLease(); return lease.State.TryGetSortedDocOrdinal(field, docId, out ordinal); }
    public bool TryGetSortedSetDocValues(string field, int docId, out IReadOnlyList<string> values)
    {
        using var lease = AcquireReadLease();
        if (!lease.State.TryGetSortedSetDocValues(field, docId, out var view)) { values = Array.Empty<string>(); return false; }
        values = view.ToArray();
        return true;
    }
    public bool TryGetSortedSetDocOrdinals(string field, int docId, out IReadOnlyList<int> ordinals)
    {
        using var lease = AcquireReadLease();
        if (!lease.State.TryGetSortedSetDocOrdinals(field, docId, out var view)) { ordinals = Array.Empty<int>(); return false; }
        ordinals = view.ToArray();
        return true;
    }
    public bool TryGetSortedNumericDocValues(string field, int docId, out IReadOnlyList<double> values)
    {
        using var lease = AcquireReadLease();
        if (!lease.State.TryGetSortedNumericDocValues(field, docId, out var view)) { values = Array.Empty<double>(); return false; }
        values = view.ToArray();
        return true;
    }
    public bool TryGetSortedInt64DocValues(string field, int docId, out IReadOnlyList<long> values)
    {
        using var lease = AcquireReadLease();
        if (!lease.State.TryGetSortedInt64DocValues(field, docId, out var view)) { values = Array.Empty<long>(); return false; }
        values = view.ToArray();
        return true;
    }
    public bool TryGetBinaryDocValues(string field, int docId, out IReadOnlyList<byte[]> values)
    {
        using var lease = AcquireReadLease();
        if (!lease.State.TryGetBinaryDocValues(field, docId, out var view)) { values = Array.Empty<byte[]>(); return false; }
        values = view.ToArray();
        return true;
    }

    internal bool HasBinaryDocValuesForEveryDocument(string field)
    {
        if (TryGetFastState(out var state))
            return state.HasBinaryDocValuesForEveryDocument(field);

        using var lease = AcquireReadLease();
        return lease.State.HasBinaryDocValuesForEveryDocument(field);
    }
    /// <summary>Returns a defensive copy of the NumericDocValues array for a field, or null if unavailable.</summary>
    public double[]? GetNumericDocValues(string field) { using var lease = AcquireReadLease(); return CloneArray(lease.State.GetNumericDocValues(field)); }

    /// <summary>Returns a defensive copy of the SortedDocValues array for a field, or null if unavailable.</summary>
    public string[]? GetSortedDocValues(string field) { using var lease = AcquireReadLease(); return CloneArray(lease.State.GetSortedDocValues(field)); }

    /// <summary>Returns a deep defensive copy of the SortedSetDocValues arrays for a field, or null if unavailable.</summary>
    public string[][]? GetSortedSetDocValues(string field) { using var lease = AcquireReadLease(); return CloneNestedArray(lease.State.GetSortedSetDocValues(field)); }

    /// <summary>Returns a defensive copy of the sorted local term dictionary, or null if unavailable.</summary>
    public string[]? GetSortedDocValueTerms(string field) { using var lease = AcquireReadLease(); return CloneArray(lease.State.GetSortedDocValueTerms(field)); }

    /// <summary>Returns an internal immutable term view for ordinal construction.</summary>
    internal IReadOnlyList<string>? GetSortedDocValueTermsView(string field)
    {
        using var lease = AcquireReadLease();
        return lease.State.GetSortedDocValueTermsView(field);
    }

    /// <summary>Returns a defensive copy of the sorted-set local term dictionary, or null if unavailable.</summary>
    public string[]? GetSortedSetDocValueTerms(string field) { using var lease = AcquireReadLease(); return CloneArray(lease.State.GetSortedSetDocValueTerms(field)); }

    /// <summary>Returns an internal immutable term view for ordinal construction.</summary>
    internal IReadOnlyList<string>? GetSortedSetDocValueTermsView(string field)
    {
        using var lease = AcquireReadLease();
        return lease.State.GetSortedSetDocValueTermsView(field);
    }

    /// <summary>Returns a deep defensive copy of the SortedNumericDocValues arrays for a field, or null if unavailable.</summary>
    public double[][]? GetSortedNumericDocValues(string field) { using var lease = AcquireReadLease(); return CloneNestedArray(lease.State.GetSortedNumericDocValues(field)); }

    /// <summary>Returns a deep defensive copy of the BinaryDocValues arrays and payloads for a field, or null if unavailable.</summary>
    public byte[][][]? GetBinaryDocValues(string field) { using var lease = AcquireReadLease(); return CloneBinaryArray(lease.State.GetBinaryDocValues(field)); }

    /// <summary>Returns a defensive copy of the Int64DocValues array for a field, or null if unavailable.</summary>
    public long[]? GetInt64DocValues(string field) { using var lease = AcquireReadLease(); return CloneArray(lease.State.GetInt64DocValues(field)); }

    /// <summary>Returns a deep defensive copy of the Int64SortedNumericDocValues arrays for a field, or null if unavailable.</summary>
    public long[][]? GetSortedInt64DocValues(string field) { using var lease = AcquireReadLease(); return CloneNestedArray(lease.State.GetSortedInt64DocValues(field)); }

    internal bool HasBinaryDocValue(string field, int docId)
    {
        if (TryGetFastState(out var fastState))
            return fastState.HasBinaryDocValue(field, docId);

        using var lease = AcquireReadLease();
        return lease.State.HasBinaryDocValue(field, docId);
    }

    private static T[]? CloneArray<T>(T[]? values)
        => values is null ? null : (T[])values.Clone();

    private static T[][]? CloneNestedArray<T>(T[][]? values)
    {
        if (values is null)
            return null;

        var copy = new T[values.Length][];
        for (int i = 0; i < values.Length; i++)
            copy[i] = (T[])values[i].Clone();
        return copy;
    }

    private static byte[][][]? CloneBinaryArray(byte[][][]? values)
    {
        if (values is null)
            return null;

        var copy = new byte[values.Length][][];
        for (int i = 0; i < values.Length; i++)
        {
            byte[][] documentValues = values[i];
            var documentCopy = new byte[documentValues.Length][];
            for (int j = 0; j < documentValues.Length; j++)
                documentCopy[j] = (byte[])documentValues[j].Clone();
            copy[i] = documentCopy;
        }

        return copy;
    }
    public bool HasNumericField(string field) { using var lease = AcquireReadLease(); return lease.State.HasNumericField(field); }
    internal bool HasNumericIndex(string field) { using var lease = AcquireReadLease(); return lease.State.HasNumericIndex(field); }
    internal bool HasInt64Index(string field) { using var lease = AcquireReadLease(); return lease.State.HasInt64Index(field); }
    public List<(int DocId, double Value)> GetNumericRange(string field, double min, double max) { using var lease = AcquireReadLease(); return lease.State.GetNumericRange(field, min, max); }
    internal bool VisitNumericRange(string field, double min, double max, Action<int, double> visitor) { using var lease = AcquireReadLease(); return lease.State.VisitNumericRange(field, min, max, visitor); }
    public List<(int DocId, double Value)> GetNumericPointsInSet(string field, IReadOnlySet<double> values) { using var lease = AcquireReadLease(); return lease.State.GetNumericPointsInSet(field, values); }
    public List<(int DocId, long Value)> GetInt64Range(string field, long min, long max) { using var lease = AcquireReadLease(); return lease.State.GetInt64Range(field, min, max); }
    internal bool VisitInt64Range(string field, long min, long max, Action<int, long> visitor) { using var lease = AcquireReadLease(); return lease.State.VisitInt64Range(field, min, max, visitor); }
    public List<(int DocId, long Value)> GetInt64PointsInSet(string field, IReadOnlySet<long> values) { using var lease = AcquireReadLease(); return lease.State.GetInt64PointsInSet(field, values); }
    public bool HasFieldValue(string field, int docId) { using var lease = AcquireReadLease(); return lease.State.HasFieldValue(field, docId); }
    public bool HasVectors { get { using var lease = AcquireReadLease(); return lease.State.HasVectors; } }
    public float[]? GetVector(int docId) { using var lease = AcquireReadLease(); return lease.State.GetVector(docId); }
    public float[]? GetVector(string fieldName, int docId) { using var lease = AcquireReadLease(); return lease.State.GetVector(fieldName, docId); }
    internal bool TryCopyVectorTo(string fieldName, int docId, Span<float> destination)
    { using var lease = AcquireReadLease(); return lease.State.TryCopyVectorTo(fieldName, docId, destination); }
    public IReadOnlyCollection<string> VectorFieldNames { get { using var lease = AcquireReadLease(); return lease.State.VectorFieldNames.ToArray(); } }
    internal HnswGraph? GetHnswGraph(string fieldName) { using var lease = AcquireReadLease(); return lease.State.GetHnswGraph(fieldName); }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposeStarted, 1, 0) != 0)
            return;
        _disposed = true;
        _operations.BeginDisposeAndWait();

        List<Exception>? failures = null;
        if (_ownsCache)
        {
            try { _cache.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }

        var snapshot = _snapshot;
        _snapshot = null;
        if (snapshot is not null)
        {
            try { snapshot.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }

        if (failures is not null)
            throw new AggregateException("Segment reader cleanup failed.", failures);
    }
}

internal struct SegmentReaderLease : IDisposable
{
    private BoundedLruCache<string, SegmentReaderState>.Lease _lease;
    private readonly SegmentReaderState _state;
    private LifetimeLease _operationLease;
    private LifetimeLease _directoryLease;

    internal SegmentReaderLease(
        BoundedLruCache<string, SegmentReaderState>.Lease lease,
        LifetimeLease operationLease,
        LifetimeLease directoryLease)
    {
        _lease = lease;
        _state = lease.Value;
        _operationLease = operationLease;
        _directoryLease = directoryLease;
    }

    internal SegmentReaderLease(SegmentReaderState state)
    {
        _lease = default;
        _state = state;
        _operationLease = default;
        _directoryLease = default;
    }

    internal SegmentReaderLease(
        SegmentReaderState state,
        LifetimeLease operationLease,
        LifetimeLease directoryLease)
    {
        _lease = default;
        _state = state;
        _operationLease = operationLease;
        _directoryLease = directoryLease;
    }

    internal SegmentReaderState State => _state;

    internal LifetimeLease DetachStateLifetimeLease() => _lease.Detach();

    public void Dispose()
    {
        _lease.Dispose();
        _operationLease.Dispose();
        _directoryLease.Dispose();
    }
}

internal struct SegmentQueryLease : IDisposable
{
    private SegmentReader.QueryPinContext? _context;
    private readonly long _token;

    internal SegmentQueryLease(SegmentReader.QueryPinContext context, long token)
    {
        _context = context;
        _token = token;
    }

    public void Dispose()
    {
        var context = _context;
        if (context is null)
            return;

        context.Release(_token);
        _context = null;
    }
}
