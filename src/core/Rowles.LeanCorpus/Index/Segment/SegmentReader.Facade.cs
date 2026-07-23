using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Store;
using System.Text.RegularExpressions;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Metadata-first facade for one immutable segment. Heavy codec state is loaded on
/// first use and retained privately or through a searcher's bounded reader cache.
/// </summary>
public sealed class SegmentReader : IDisposable
{
    [ThreadStatic] private static SegmentReader? t_pinnedReader;
    [ThreadStatic] private static SegmentReaderState? t_pinnedState;
    [ThreadStatic] private static int t_pinDepth;

    private readonly MMapDirectory _directory;
    private readonly SegmentInfo _info;
    private readonly BoundedLruCache<string, SegmentReaderState> _cache;
    private readonly Func<SegmentReaderState> _stateFactory;
    private readonly bool _ownsCache;
    private readonly bool _permanentlyResident;
    private SegmentReaderState? _residentState;
    private FileSnapshotLease? _snapshot;
    private bool _disposed;

    /// <summary>Gets or sets the document base offset for this reader within the global document namespace.</summary>
    public int DocBase { get; set; }

    /// <summary>Gets the segment metadata for this reader.</summary>
    public SegmentInfo Info => _info;

    /// <summary>Gets the directory this reader was opened from.</summary>
    internal MMapDirectory Directory => _directory;

    /// <summary>Gets the total number of documents in this segment, including deleted documents.</summary>
    public int MaxDoc => _info.DocCount;

    /// <summary>Creates a lazy reader that privately retains its heavy state.</summary>
    public SegmentReader(MMapDirectory directory, SegmentInfo info)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(info);
        var segmentId = info.SegmentId;
        var snapshot = directory.AcquireSnapshot(
            name => IsSegmentFile(segmentId, name), out var inventory);
        try
        {
            ValidateRequiredFiles(info, inventory);
            _snapshot = snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
        _directory = directory;
        _info = info;
        _cache = new BoundedLruCache<string, SegmentReaderState>(1, StringComparer.Ordinal);
        _stateFactory = () => new SegmentReaderState(directory, info);
        _ownsCache = true;
        _permanentlyResident = true;
    }

    internal SegmentReader(
        MMapDirectory directory,
        SegmentInfo info,
        BoundedLruCache<string, SegmentReaderState> cache,
        IReadOnlyCollection<string> inventory,
        bool permanentlyResident)
    {
        ValidateRequiredFiles(info, inventory);
        _directory = directory;
        _info = info;
        _cache = cache;
        _stateFactory = () => new SegmentReaderState(directory, info);
        _permanentlyResident = permanentlyResident;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal SegmentReaderLease AcquireReadLease()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(t_pinnedReader, this) && t_pinnedState is not null)
            return new SegmentReaderLease(t_pinnedState);
        if (Volatile.Read(ref _residentState) is { } resident)
            return new SegmentReaderLease(resident);
        var lease = _cache.Acquire(_info.SegmentId, _stateFactory);
        if (_permanentlyResident)
            Volatile.Write(ref _residentState, lease.Value);
        return new SegmentReaderLease(lease);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private bool TryGetFastState([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SegmentReaderState? state)
    {
        if (ReferenceEquals(t_pinnedReader, this) && t_pinnedState is not null)
        {
            state = t_pinnedState;
            return true;
        }
        state = Volatile.Read(ref _residentState);
        return state is not null;
    }

    internal SegmentQueryLease AcquireQueryLease()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(t_pinnedReader, this) && t_pinnedState is not null)
        {
            t_pinDepth++;
            return new SegmentQueryLease(this, default, ownsCacheLease: false);
        }

        if (Volatile.Read(ref _residentState) is { } resident)
        {
            t_pinnedReader = this;
            t_pinnedState = resident;
            t_pinDepth = 1;
            return new SegmentQueryLease(this, default, ownsCacheLease: false);
        }

        var cacheLease = _cache.Acquire(_info.SegmentId, _stateFactory);
        if (_permanentlyResident)
            Volatile.Write(ref _residentState, cacheLease.Value);
        t_pinnedReader = this;
        t_pinnedState = cacheLease.Value;
        t_pinDepth = 1;
        return new SegmentQueryLease(this, cacheLease, ownsCacheLease: true);
    }

    internal void ReleaseQueryLease(
        BoundedLruCache<string, SegmentReaderState>.Lease cacheLease,
        bool ownsCacheLease)
    {
        if (--t_pinDepth == 0)
        {
            t_pinnedReader = null;
            t_pinnedState = null;
        }
        if (ownsCacheLease)
            cacheLease.Dispose();
    }

    internal static string[] SelectSegmentFiles(string segmentId, IReadOnlyCollection<string> inventory)
        => inventory.Where(name => IsSegmentFile(segmentId, name))
            .ToArray();

    private static bool IsSegmentFile(string segmentId, string name)
        => name.Equals(segmentId + ".seg", StringComparison.Ordinal)
            || name.StartsWith(segmentId + ".", StringComparison.Ordinal)
            || name.StartsWith(segmentId + "_", StringComparison.Ordinal);

    internal static void ValidateRequiredFiles(SegmentInfo info, IReadOnlyCollection<string> inventory)
    {
        var files = inventory is HashSet<string> set
            ? set
            : new HashSet<string>(inventory, StringComparer.Ordinal);
        foreach (var extension in new[] { ".seg", ".dic", ".pos", ".nrm" })
        {
            var name = info.SegmentId + extension;
            if (!files.Contains(name))
                throw new FileNotFoundException($"Segment file is missing: '{name}'.", name);
        }
    }

    public bool IsLive(int docId) { if (TryGetFastState(out var state)) return state.IsLive(docId); using var lease = AcquireReadLease(); return lease.State.IsLive(docId); }
    public bool IsSoftDeleted(int docId, out long timestamp) { using var lease = AcquireReadLease(); return lease.State.IsSoftDeleted(docId, out timestamp); }
    public bool HasDeletions { get { if (TryGetFastState(out var state)) return state.HasDeletions; using var lease = AcquireReadLease(); return lease.State.HasDeletions; } }
    internal ParentBitSet? GetParentBitSet() { using var lease = AcquireReadLease(); return lease.State.GetParentBitSet(); }
    public float GetNorm(int docId, string field) { using var lease = AcquireReadLease(); return lease.State.GetNorm(docId, field); }
    public float GetFieldBoost(int docId, string field) { using var lease = AcquireReadLease(); return lease.State.GetFieldBoost(docId, field); }
    internal bool TryGetFieldBoosts(string field, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out float[]? boosts) { if (TryGetFastState(out var state)) return state.TryGetFieldBoosts(field, out boosts); using var lease = AcquireReadLease(); return lease.State.TryGetFieldBoosts(field, out boosts); }
    public int GetFieldLength(int docId, string field) { using var lease = AcquireReadLease(); return lease.State.GetFieldLength(docId, field); }
    public bool TryGetFieldLengths(string field, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out int[]? lengths) { if (TryGetFastState(out var state)) return state.TryGetFieldLengths(field, out lengths); using var lease = AcquireReadLease(); return lease.State.TryGetFieldLengths(field, out lengths); }
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
            postings.AttachLifetimeLease(lease.DetachLifetimeLease());
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

    public bool TryGetNumericValue(string field, int docId, out double value) { using var lease = AcquireReadLease(); return lease.State.TryGetNumericValue(field, docId, out value); }
    public bool TryGetInt64Value(string field, int docId, out long value) { using var lease = AcquireReadLease(); return lease.State.TryGetInt64Value(field, docId, out value); }
    public bool TryGetSortedDocValue(string field, int docId, out string value) { using var lease = AcquireReadLease(); return lease.State.TryGetSortedDocValue(field, docId, out value); }
    public bool TryGetSortedSetDocValues(string field, int docId, out IReadOnlyList<string> values) { using var lease = AcquireReadLease(); return lease.State.TryGetSortedSetDocValues(field, docId, out values); }
    public bool TryGetSortedNumericDocValues(string field, int docId, out IReadOnlyList<double> values) { using var lease = AcquireReadLease(); return lease.State.TryGetSortedNumericDocValues(field, docId, out values); }
    public bool TryGetSortedInt64DocValues(string field, int docId, out IReadOnlyList<long> values) { using var lease = AcquireReadLease(); return lease.State.TryGetSortedInt64DocValues(field, docId, out values); }
    public bool TryGetBinaryDocValues(string field, int docId, out IReadOnlyList<byte[]> values) { using var lease = AcquireReadLease(); return lease.State.TryGetBinaryDocValues(field, docId, out values); }
    public double[]? GetNumericDocValues(string field) { using var lease = AcquireReadLease(); return lease.State.GetNumericDocValues(field); }
    public string[]? GetSortedDocValues(string field) { using var lease = AcquireReadLease(); return lease.State.GetSortedDocValues(field); }
    public string[][]? GetSortedSetDocValues(string field) { using var lease = AcquireReadLease(); return lease.State.GetSortedSetDocValues(field); }
    public double[][]? GetSortedNumericDocValues(string field) { using var lease = AcquireReadLease(); return lease.State.GetSortedNumericDocValues(field); }
    public byte[][][]? GetBinaryDocValues(string field) { using var lease = AcquireReadLease(); return lease.State.GetBinaryDocValues(field); }
    public long[]? GetInt64DocValues(string field) { using var lease = AcquireReadLease(); return lease.State.GetInt64DocValues(field); }
    public long[][]? GetSortedInt64DocValues(string field) { using var lease = AcquireReadLease(); return lease.State.GetSortedInt64DocValues(field); }
    public bool HasNumericField(string field) { using var lease = AcquireReadLease(); return lease.State.HasNumericField(field); }
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
    public IReadOnlyCollection<string> VectorFieldNames { get { using var lease = AcquireReadLease(); return lease.State.VectorFieldNames.ToArray(); } }
    internal HnswGraph? GetHnswGraph(string fieldName) { using var lease = AcquireReadLease(); return lease.State.GetHnswGraph(fieldName); }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsCache)
            _cache.Dispose();
        _snapshot?.Dispose();
        _snapshot = null;
    }
}

internal struct SegmentReaderLease : IDisposable
{
    private BoundedLruCache<string, SegmentReaderState>.Lease _lease;
    private readonly SegmentReaderState _state;

    internal SegmentReaderLease(BoundedLruCache<string, SegmentReaderState>.Lease lease)
    {
        _lease = lease;
        _state = lease.Value;
    }

    internal SegmentReaderLease(SegmentReaderState state)
    {
        _lease = default;
        _state = state;
    }

    internal SegmentReaderState State => _state;

    internal LifetimeLease DetachLifetimeLease() => _lease.Detach();

    public void Dispose() => _lease.Dispose();
}

internal struct SegmentQueryLease : IDisposable
{
    private SegmentReader? _reader;
    private BoundedLruCache<string, SegmentReaderState>.Lease _cacheLease;
    private readonly bool _ownsCacheLease;

    internal SegmentQueryLease(
        SegmentReader reader,
        BoundedLruCache<string, SegmentReaderState>.Lease cacheLease,
        bool ownsCacheLease)
    {
        _reader = reader;
        _cacheLease = cacheLease;
        _ownsCacheLease = ownsCacheLease;
    }

    public void Dispose()
    {
        var reader = Interlocked.Exchange(ref _reader, null);
        reader?.ReleaseQueryLease(_cacheLease, _ownsCacheLease);
    }
}
