using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// One search's shared prefix, wildcard and regexp term discovery. Admission and global-statistics
/// preparation record each reader's matching qualified terms and postings offsets; execution reuses
/// them, so a search walks each reader's dictionary once for a clause instead of once per phase.
/// </summary>
/// <remarks>
/// Entries are keyed by query instance and reader instance, never by query value, so differently
/// configured queries cannot share terms. Lists are immutable after publication and live only for the
/// search, so no entry outlives the readers it was taken from. Regexp lists may be candidate supersets
/// (the contains route keeps terms with line feeds); the regexp executor verifies every term it uses.
/// </remarks>
internal sealed class MultiTermExpansionContext
{
    // Created on first store, so searches without prefix, wildcard or regexp clauses allocate nothing here.
    private ConcurrentDictionary<Key, IReadOnlyList<(string Term, long Offset)>>? _expansions;

    internal static bool IsShareable(Query query) => query is PrefixQuery or WildcardQuery or RegexpQuery;

    internal bool TryGet(Query query, SegmentReader reader, out IReadOnlyList<(string Term, long Offset)> terms)
    {
        if (Volatile.Read(ref _expansions) is { } expansions)
            return expansions.TryGetValue(new Key(query, reader), out terms!);
        terms = [];
        return false;
    }

    internal void Store(Query query, SegmentReader reader, IReadOnlyList<(string Term, long Offset)> terms)
    {
        var expansions = Volatile.Read(ref _expansions)
            ?? Interlocked.CompareExchange(ref _expansions, new(), null)
            ?? _expansions!;
        expansions.TryAdd(new Key(query, reader), terms);
    }

    private readonly struct Key(Query query, SegmentReader reader) : IEquatable<Key>
    {
        private readonly Query _query = query;
        private readonly SegmentReader _reader = reader;

        public bool Equals(Key other) => ReferenceEquals(_query, other._query) && ReferenceEquals(_reader, other._reader);
        public override bool Equals(object? obj) => obj is Key other && Equals(other);
        public override int GetHashCode()
            => HashCode.Combine(RuntimeHelpers.GetHashCode(_query), RuntimeHelpers.GetHashCode(_reader));
    }
}
