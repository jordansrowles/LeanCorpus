using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Thread-safe query result cache. Entries are keyed by (Query, topN) and
/// invalidated when the commit generation changes. Uses
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> with generation-swap eviction:
/// readers are lock-free on the common path, and when the soft entry cap is
/// exceeded the entire dictionary is swapped for a fresh one via
/// <see cref="Interlocked.CompareExchange(ref object,object,object)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Invalidation contract: the cache is keyed against a single commit generation.
/// Callers must invoke <see cref="Invalidate"/> (or assign a fresh cache) whenever
/// the underlying searcher is swapped to a newer commit, otherwise stale results
/// may be returned.
/// </para>
/// <para>
/// Recommended placement: hold one cache per <see cref="SearcherManager"/> rather
/// than per <see cref="IndexSearcher"/>, and call <see cref="Invalidate"/> from the
/// refresh hook.
/// </para>
/// </remarks>
public sealed class QueryCache
{
    private readonly int _maxEntries;
    private volatile ConcurrentDictionary<CacheKey, CacheEntry> _map;
    private readonly Lock _swapLock = new();
    private long _generation;
    private long _hits;
    private long _misses;
    private int _approxCount;

    /// <summary>
    /// Initialises a new <see cref="QueryCache"/> with the specified maximum entry count.
    /// </summary>
    /// <param name="maxEntries">The maximum number of entries to hold. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="maxEntries"/> is less than 1.</exception>
    public QueryCache(int maxEntries = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        _maxEntries = maxEntries;
        _map = new ConcurrentDictionary<CacheKey, CacheEntry>(Environment.ProcessorCount, maxEntries);
    }

    /// <summary>Total cache hits since creation.</summary>
    public long Hits => Interlocked.Read(ref _hits);

    /// <summary>Total cache misses since creation.</summary>
    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>Approximate number of cached entries.</summary>
    public int Count => Volatile.Read(ref _approxCount);

    /// <summary>
    /// Tries to retrieve a cached result. Returns null on miss.
    /// Lock-free on the read path.
    /// </summary>
    public TopDocs? TryGet(Query query, int topN)
    {
        var key = new CacheKey(QueryFingerprint.Create(query), topN);
        var map = _map;
        long gen = Interlocked.Read(ref _generation);

        if (map.TryGetValue(key, out var entry) && entry.Generation == gen)
        {
            Interlocked.Increment(ref _hits);
            return entry.Result;
        }

        Interlocked.Increment(ref _misses);
        return null;
    }

    /// <summary>
    /// Stores a query result in the cache. If the soft entry cap is exceeded,
    /// the entire dictionary is swapped for a fresh one.
    /// </summary>
    public void Put(Query query, int topN, TopDocs result)
    {
        var key = new CacheKey(QueryFingerprint.Create(query), topN);
        long gen = Interlocked.Read(ref _generation);
        var entry = new CacheEntry(key, result, gen);

        var map = _map;
        bool isNew = !map.ContainsKey(key);
        map[key] = entry; // AddOrUpdate semantics via indexer
        int newCount = isNew ? Interlocked.Increment(ref _approxCount) : Volatile.Read(ref _approxCount);

        if (newCount > _maxEntries)
            EvictGeneration(map);
    }

    /// <summary>
    /// Invalidates all cached entries by bumping the generation.
    /// Lazy invalidation: stale entries are skipped on next access.
    /// </summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
    }

    /// <summary>Clears all entries and resets counters.</summary>
    public void Clear()
    {
        SwapMap(new ConcurrentDictionary<CacheKey, CacheEntry>(
            Environment.ProcessorCount, _maxEntries));
        Interlocked.Increment(ref _generation);
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
        Volatile.Write(ref _approxCount, 0);
    }

    private void EvictGeneration(ConcurrentDictionary<CacheKey, CacheEntry> mapToEvict)
    {
        lock (_swapLock)
        {
            if (_map != mapToEvict)
                return; // Another thread already swapped.

            if (_approxCount <= _maxEntries)
                return; // Shrank below threshold concurrently.

            var fresh = new ConcurrentDictionary<CacheKey, CacheEntry>(
                Environment.ProcessorCount, _maxEntries);
            SwapMap(fresh);
            Volatile.Write(ref _approxCount, 0);
        }
    }

    private void SwapMap(ConcurrentDictionary<CacheKey, CacheEntry> fresh)
    {
        Interlocked.Exchange(ref _map, fresh);
    }

    private readonly record struct CacheKey(string QueryFingerprint, int TopN);

    private sealed class CacheEntry(CacheKey key, TopDocs result, long generation)
    {
        public CacheKey Key { get; } = key;
        public TopDocs Result { get; } = result;
        public long Generation { get; } = generation;
    }

    private static class QueryFingerprint
    {
        public static string Create(Query query)
        {
            var builder = new StringBuilder(128);
            Append(query, builder);
            return builder.ToString();
        }

        private static void Append(Query query, StringBuilder builder)
        {
            builder.Append(query.GetType().Name)
                .Append("|b=")
                .Append(query.Boost.ToString("R", CultureInfo.InvariantCulture));

            switch (query)
            {
                case TermQuery tq:
                    AppendPart(builder, tq.Field);
                    AppendPart(builder, tq.Term);
                    break;
                case PhraseQuery pq:
                    AppendPart(builder, pq.Field);
                    builder.Append("|slop=").Append(pq.Slop);
                    foreach (var term in pq.Terms) AppendPart(builder, term);
                    break;
                case MultiPhraseQuery mpq:
                    AppendPart(builder, mpq.Field);
                    builder.Append("|slop=").Append(mpq.Slop);
                    for (int i = 0; i < mpq.TermGroups.Count; i++)
                    {
                        builder.Append("|pos=").Append(mpq.Positions[i]).Append('(');
                        foreach (var term in mpq.TermGroups[i]) AppendPart(builder, term);
                        builder.Append(')');
                    }
                    break;
                case BooleanQuery bq:
                    foreach (var clause in bq.Clauses)
                    {
                        builder.Append("|").Append(clause.Occur).Append("(");
                        Append(clause.Query, builder);
                        builder.Append(')');
                    }
                    break;
                case ConstantScoreQuery csq:
                    builder.Append("|score=").Append(csq.ConstantScore.ToString("R", CultureInfo.InvariantCulture)).Append("(");
                    Append(csq.Inner, builder);
                    builder.Append(')');
                    break;
                case DisjunctionMaxQuery dmq:
                    builder.Append("|tie=").Append(dmq.TieBreakerMultiplier.ToString("R", CultureInfo.InvariantCulture));
                    foreach (var disjunct in dmq.Disjuncts)
                    {
                        builder.Append("(");
                        Append(disjunct, builder);
                        builder.Append(')');
                    }
                    break;
                case RangeQuery rq:
                    AppendPart(builder, rq.Field);
                    builder.Append("|min=").Append(rq.Min.ToString("R", CultureInfo.InvariantCulture));
                    builder.Append("|max=").Append(rq.Max.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case MatchAllDocsQuery:
                    break;
                case MatchNoDocsQuery mndq:
                    AppendPart(builder, mndq.Reason);
                    break;
                case FieldExistsQuery feq:
                    AppendPart(builder, feq.Field);
                    break;
                case TermInSetQuery tisq:
                    AppendPart(builder, tisq.Field);
                    foreach (var term in tisq.Terms) AppendPart(builder, term);
                    break;
                case PointInSetQuery pisq:
                    AppendPart(builder, pisq.Field);
                    foreach (var point in pisq.Points)
                        builder.Append("|pt=").Append(point.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case PrefixQuery pq:
                    AppendPart(builder, pq.Field);
                    AppendPart(builder, pq.Prefix);
                    break;
                case WildcardQuery wq:
                    AppendPart(builder, wq.Field);
                    AppendPart(builder, wq.Pattern);
                    break;
                case FuzzyQuery fq:
                    AppendPart(builder, fq.Field);
                    AppendPart(builder, fq.Term);
                    builder.Append("|edits=").Append(fq.MaxEdits).Append("|exp=").Append(fq.MaxExpansions);
                    break;
                case FunctionScoreQuery fsq:
                    AppendPart(builder, fsq.NumericField);
                    builder.Append("|mode=").Append(fsq.Mode).Append("(");
                    Append(fsq.Inner, builder);
                    builder.Append(')');
                    break;
                case BlockJoinQuery bjq:
                    builder.Append("|child=(");
                    Append(bjq.ChildQuery, builder);
                    builder.Append(')');
                    break;
                case RrfQuery rrf:
                    builder.Append("|k=").Append(rrf.K);
                    foreach (var child in rrf.Queries)
                    {
                        builder.Append("(");
                        Append(child, builder);
                        builder.Append(')');
                    }
                    break;
                case VectorQuery vq:
                    AppendPart(builder, vq.Field);
                    builder.Append("|topK=").Append(vq.TopK)
                        .Append("|ef=").Append(vq.EfSearch)
                        .Append("|over=").Append(vq.OversamplingFactor)
                        .Append("|vec=");
                    foreach (float value in vq.QueryVector)
                        builder.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(',');
                    if (vq.Filter is not null)
                    {
                        builder.Append("|filter=(");
                        Append(vq.Filter, builder);
                        builder.Append(')');
                    }
                    break;
                case CombinedFieldsQuery cfq:
                    builder.Append("|msm=").Append(cfq.MinimumShouldMatch);
                    foreach (var field in cfq.Fields) AppendPart(builder, field);
                    foreach (var term in cfq.Terms) AppendPart(builder, term);
                    foreach (var fieldWeight in cfq.FieldWeights.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                    {
                        AppendPart(builder, fieldWeight.Key);
                        builder.Append("|w=").Append(fieldWeight.Value.ToString("R", CultureInfo.InvariantCulture));
                    }
                    break;
                case IntervalsQuery iq:
                    builder.Append("|src=");
                    AppendIntervalsSource(iq.Source, builder);
                    break;
                default:
                    AppendPart(builder, query.Field);
                    builder.Append("|hash=").Append(query.GetHashCode());
                    break;
            }
        }

        private static void AppendPart(StringBuilder builder, string value)
            => builder.Append('|').Append(value.Length).Append(':').Append(value);

        private static void AppendIntervalsSource(IntervalsSource source, StringBuilder builder)
        {
            builder.Append(source.GetType().Name);
            switch (source)
            {
                case IntervalsTermSource termSource:
                    AppendPart(builder, termSource.Field);
                    AppendPart(builder, termSource.Term);
                    break;
                case IntervalsPhraseSource phraseSource:
                    AppendPart(builder, phraseSource.Field);
                    foreach (var term in phraseSource.Terms) AppendPart(builder, term);
                    break;
                case IntervalsOrSource orSource:
                    foreach (var child in orSource.Sources)
                    {
                        builder.Append('(');
                        AppendIntervalsSource(child, builder);
                        builder.Append(')');
                    }
                    break;
                case IntervalsOrderedSource orderedSource:
                    builder.Append("|g=").Append(orderedSource.MaxGaps);
                    foreach (var child in orderedSource.Sources)
                    {
                        builder.Append('(');
                        AppendIntervalsSource(child, builder);
                        builder.Append(')');
                    }
                    break;
                case IntervalsUnorderedSource unorderedSource:
                    builder.Append("|g=").Append(unorderedSource.MaxGaps);
                    foreach (var child in unorderedSource.Sources)
                    {
                        builder.Append('(');
                        AppendIntervalsSource(child, builder);
                        builder.Append(')');
                    }
                    break;
                case IntervalsContainingSource containingSource:
                    builder.Append('(');
                    AppendIntervalsSource(containingSource.Outer, builder);
                    builder.Append(")(");
                    AppendIntervalsSource(containingSource.Inner, builder);
                    builder.Append(')');
                    break;
                case IntervalsContainedBySource containedBySource:
                    builder.Append('(');
                    AppendIntervalsSource(containedBySource.Inner, builder);
                    builder.Append(")(");
                    AppendIntervalsSource(containedBySource.Outer, builder);
                    builder.Append(')');
                    break;
                case IntervalsNotContainingSource notContainingSource:
                    builder.Append('(');
                    AppendIntervalsSource(notContainingSource.Outer, builder);
                    builder.Append(")(");
                    AppendIntervalsSource(notContainingSource.Inner, builder);
                    builder.Append(')');
                    break;
            }
        }
    }
}
