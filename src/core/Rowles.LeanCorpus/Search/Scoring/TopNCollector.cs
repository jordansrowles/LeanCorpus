using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Index.Segment;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Bounded min-heap collector that keeps the top-N highest-scoring documents.
/// Single allocation: the ScoreDoc[topN] backing array.
/// </summary>
public struct TopNCollector
{
    private readonly ScoreDoc[] _heap;
    private readonly int _maxSize;
    private readonly ITopNCollectorStrategy? _strategy;
    private readonly ISideCollector? _sideCollector;
    private SegmentReader? _sideCollectorReader;
    private int _size;
    private float _minScore;
    private int _totalHits;

    /// <summary>Gets the total number of documents passed to <see cref="Collect"/>.</summary>
    public int TotalHits => _strategy?.TotalHits ?? _totalHits;

    /// <summary>Gets the maximum number of documents this collector can retain.</summary>
    public int Capacity => _strategy?.Capacity ?? _maxSize;
    /// <summary>True when the collector has reached its maximum capacity.</summary>
    public bool IsFull => _strategy?.IsFull ?? _size >= _maxSize;

    /// <summary>Gets whether this collector fans every match into an exhaustive side collector.</summary>
    internal bool HasSideCollector => _sideCollector is not null;

    /// <summary>Gets the score of the lowest-ranked document currently in the top-N, or <see cref="float.NegativeInfinity"/> if fewer than N documents have been collected.</summary>
    public float MinScore => _strategy?.MinScore ?? _minScore;

    /// <summary>Initialises a new <see cref="TopNCollector"/> with the specified capacity.</summary>
    /// <param name="maxSize">Maximum number of top-scoring documents to retain.</param>
    public TopNCollector(int maxSize)
        : this(maxSize, sideCollector: null)
    {
    }

    internal TopNCollector(int maxSize, ISideCollector? sideCollector)
    {
        _heap = new ScoreDoc[maxSize];
        _maxSize = maxSize;
        _strategy = null;
        _sideCollector = sideCollector;
        _sideCollectorReader = null;
        _size = 0;
        _minScore = float.NegativeInfinity;
        _totalHits = 0;
    }

    /// <summary>Constructs a collector using an externally-owned backing array (avoids allocation).</summary>
    public TopNCollector(ScoreDoc[] heap, int maxSize)
        : this(heap, maxSize, sideCollector: null)
    {
    }

    internal TopNCollector(ScoreDoc[] heap, int maxSize, ISideCollector? sideCollector)
    {
        _heap = heap;
        _maxSize = maxSize;
        _strategy = null;
        _sideCollector = sideCollector;
        _sideCollectorReader = null;
        _size = 0;
        _minScore = float.NegativeInfinity;
        _totalHits = 0;
    }

    internal TopNCollector(ITopNCollectorStrategy strategy)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _sideCollector = null;
        _sideCollectorReader = null;
        _heap = [];
        _maxSize = 0;
        _size = 0;
        _minScore = float.NegativeInfinity;
        _totalHits = 0;
    }

    /// <summary>Resets state for reuse, keeping the backing array.</summary>
    public void Reset()
    {
        if (_strategy is not null)
        {
            _strategy.Reset();
            return;
        }

        _size = 0;
        _minScore = float.NegativeInfinity;
        _totalHits = 0;
    }

    /// <summary>Sets the segment context used for an exhaustive side collector.</summary>
    internal void SetSideCollectorContext(SegmentReader reader)
    {
        if (_sideCollector is not null)
            _sideCollectorReader = reader;
    }

    /// <summary>Collects a matching document and its score, keeping only the top-N by score.</summary>
    /// <param name="docId">The internal document identifier.</param>
    /// <param name="score">The relevance score for this document.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Collect(int docId, float score)
    {
        if (_sideCollector is not null)
        {
            var reader = _sideCollectorReader
                ?? throw new InvalidOperationException("A side collector requires a segment context.");
            _sideCollector.Collect(docId, score, reader, docId - reader.DocBase);
        }

        if (_strategy is not null)
        {
            _strategy.Collect(docId, score);
            return;
        }

        _totalHits++;

        // Count-only mode: zero allocation, no heap maintenance.
        if (_maxSize == 0) return;

        if (_size < _maxSize)
        {
            _heap[_size++] = new ScoreDoc(docId, score);
            if (_size == _maxSize)
            {
                BuildMinHeap();
                _minScore = _heap[0].Score;
            }
            return;
        }

        if (score > _minScore || (score == _minScore && docId < _heap[0].DocId))
        {
            _heap[0] = new ScoreDoc(docId, score);
            SiftDown(0);
            _minScore = _heap[0].Score;
        }
    }

    /// <summary>Materialises the collected results as a <see cref="TopDocs"/> sorted by score descending.</summary>
    /// <returns>A <see cref="TopDocs"/> containing the top-N scored documents.</returns>
    public TopDocs ToTopDocs()
    {
        if (_strategy is not null)
            return _strategy.ToTopDocs();

        if (_size == 0)
            return TopDocs.Empty;

        var results = new ScoreDoc[_size];
        Array.Copy(_heap, results, _size);
        Array.Sort(results, static (a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score);
            return cmp != 0 ? cmp : a.DocId.CompareTo(b.DocId);
        });
        return new TopDocs(_totalHits, results);
    }

    private void BuildMinHeap()
    {
        for (int i = _size / 2 - 1; i >= 0; i--)
            SiftDown(i);
    }

    private void SiftDown(int i)
    {
        while (true)
        {
            int smallest = i;
            int left = 2 * i + 1;
            int right = 2 * i + 2;

            if (left < _size && LessThan(_heap[left], _heap[smallest]))
                smallest = left;
            if (right < _size && LessThan(_heap[right], _heap[smallest]))
                smallest = right;

            if (smallest == i)
                break;

            (_heap[i], _heap[smallest]) = (_heap[smallest], _heap[i]);
            i = smallest;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool LessThan(ScoreDoc a, ScoreDoc b)
    {
        // Min-heap: lowest score at root (gets evicted first)
        int cmp = a.Score.CompareTo(b.Score);
        return cmp < 0 || (cmp == 0 && a.DocId > b.DocId);
    }
}

internal interface ITopNCollectorStrategy
{
    int TotalHits { get; }
    int Capacity { get; }
    bool IsFull { get; }
    float MinScore { get; }
    void Collect(int docId, float score);
    TopDocs ToTopDocs();
    void Reset();
}
