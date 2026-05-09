using System.Runtime.CompilerServices;

namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// Bounded min-heap collector that keeps the top-N highest-scoring documents.
/// Single allocation: the ScoreDoc[topN] backing array.
/// </summary>
public struct TopNCollector
{
    private readonly ScoreDoc[] _heap;
    private readonly int _maxSize;
    private int _size;
    private float _minScore;

    /// <summary>Gets the total number of documents passed to <see cref="Collect"/>.</summary>
    public int TotalHits { get; private set; }

    /// <summary>Gets the maximum number of documents this collector can retain.</summary>
    public int Capacity => _maxSize;

    /// <summary>Initialises a new <see cref="TopNCollector"/> with the specified capacity.</summary>
    /// <param name="maxSize">Maximum number of top-scoring documents to retain.</param>
    public TopNCollector(int maxSize)
    {
        _heap = new ScoreDoc[maxSize];
        _maxSize = maxSize;
        _size = 0;
        _minScore = float.NegativeInfinity;
        TotalHits = 0;
    }

    /// <summary>Constructs a collector using an externally-owned backing array (avoids allocation).</summary>
    public TopNCollector(ScoreDoc[] heap, int maxSize)
    {
        _heap = heap;
        _maxSize = maxSize;
        _size = 0;
        _minScore = float.NegativeInfinity;
        TotalHits = 0;
    }

    /// <summary>Resets state for reuse, keeping the backing array.</summary>
    public void Reset()
    {
        _size = 0;
        _minScore = float.NegativeInfinity;
        TotalHits = 0;
    }

    /// <summary>Collects a matching document and its score, keeping only the top-N by score.</summary>
    /// <param name="docId">The internal document identifier.</param>
    /// <param name="score">The relevance score for this document.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Collect(int docId, float score)
    {
        TotalHits++;

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
        if (_size == 0)
            return TopDocs.Empty;

        var results = new ScoreDoc[_size];
        Array.Copy(_heap, results, _size);
        Array.Sort(results, static (a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score);
            return cmp != 0 ? cmp : a.DocId.CompareTo(b.DocId);
        });
        return new TopDocs(TotalHits, results);
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
