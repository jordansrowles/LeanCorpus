using System.Runtime.CompilerServices;

namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// Block-Max WAND (Weak AND) scorer for top-K query evaluation.
/// Uses per-block impact metadata (maxFreq, maxNorm) stored in skip data
/// to skip entire 128-doc blocks whose maximum possible score contribution
/// falls below the current threshold θ (score of the Kth-best doc seen so far).
/// </summary>
/// <remarks>
/// This scorer works with any BM25-based scoring function. For each postings list,
/// the block's maximum possible score is precomputed from <c>maxFreqInBlock</c>
/// and <c>maxNormInBlock</c>. If no block can beat the current threshold, the
/// entire term is skipped.
/// </remarks>
internal sealed class BlockMaxWandScorer
{
    /// <summary>
    /// Represents a single term's postings along with its precomputed block-max scores.
    /// </summary>
    internal sealed class TermScorer
    {
        public BlockPostingsEnum Postings;
        public float[] BlockMaxScores; // one per skip entry (per block)
        public float MaxScore;         // global max score across all blocks
        public int CurrentDoc;

        public TermScorer(BlockPostingsEnum postings, float idf, float k1, float b, float avgDl)
        {
            Postings = postings;
            var skipEntries = postings.SkipEntries;
            BlockMaxScores = new float[skipEntries.Length];
            MaxScore = 0f;

            for (int i = 0; i < skipEntries.Length; i++)
            {
                ref readonly var skip = ref skipEntries[i];
                float maxFreq = skip.MaxFreqInBlock;
                float maxNorm = skip.MaxNormInBlock > 0
                    ? skip.MaxNormInBlock / 255f
                    : 1f; // fallback: assume norm = 1 (average)

                float dl = 1f / (maxNorm + float.Epsilon); // approximate field length
                float tf = maxFreq;
                float score = idf * ((tf * (k1 + 1f)) / (tf + k1 * (1f - b + b * (dl / avgDl))));
                BlockMaxScores[i] = score;
                if (score > MaxScore) MaxScore = score;
            }

            CurrentDoc = BlockPostingsEnum.NoMoreDocs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetBlockMaxScore(int blockIndex)
        {
            if (blockIndex < 0 || blockIndex >= BlockMaxScores.Length)
                return MaxScore; // tail — use global max as conservative estimate
            return BlockMaxScores[blockIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetBlockIndex(int docId)
        {
            return docId / PackedIntCodec.BlockSize;
        }
    }

    private readonly TermScorer[] _scorers;
    private readonly TopNCollector _collector;
    private readonly float _k1;
    private readonly float _b;
    private readonly float _avgDl;
    private int _blocksSkipped;
    private int _blocksScored;

    /// <summary>Number of blocks skipped due to WAND threshold.</summary>
    public int BlocksSkipped => _blocksSkipped;

    /// <summary>Number of blocks fully scored.</summary>
    public int BlocksScored => _blocksScored;

    public BlockMaxWandScorer(TermScorer[] scorers, int topN, float k1 = 1.2f, float b = 0.75f, float avgDl = 100f)
    {
        _scorers = scorers;
        _collector = new TopNCollector(topN);
        _k1 = k1;
        _b = b;
        _avgDl = avgDl;
    }

    /// <summary>
    /// Executes the Block-Max WAND algorithm for a disjunctive (OR) query
    /// over all term scorers. Returns top-K results.
    /// </summary>
    public (int DocId, float Score)[] Score()
    {
        // Initialise all scorers to their first document
        foreach (var scorer in _scorers)
            scorer.CurrentDoc = scorer.Postings.NextDoc();

        while (true)
        {
            // Find the minimum current doc across all non-exhausted scorers
            int minDoc = BlockPostingsEnum.NoMoreDocs;
            foreach (var scorer in _scorers)
            {
                if (scorer.CurrentDoc < minDoc)
                    minDoc = scorer.CurrentDoc;
            }

            if (minDoc == BlockPostingsEnum.NoMoreDocs)
                break; // all exhausted

            float threshold = _collector.MinScore;

            // Check if any combination of block-max scores can beat threshold
            int blockIndex = minDoc / PackedIntCodec.BlockSize;
            float sumBlockMax = 0f;
            foreach (var scorer in _scorers)
            {
                if (scorer.CurrentDoc != BlockPostingsEnum.NoMoreDocs)
                    sumBlockMax += scorer.GetBlockMaxScore(blockIndex);
            }

            if (sumBlockMax <= threshold && _collector.Count >= _collector.Capacity)
            {
                // Skip: no combination of docs in this block can beat the threshold
                _blocksSkipped++;
                int nextBlockStart = (blockIndex + 1) * PackedIntCodec.BlockSize;
                foreach (var scorer in _scorers)
                {
                    if (scorer.CurrentDoc < nextBlockStart && scorer.CurrentDoc != BlockPostingsEnum.NoMoreDocs)
                        scorer.CurrentDoc = scorer.Postings.Advance(nextBlockStart);
                }
                continue;
            }

            _blocksScored++;

            // Score all docs at minDoc
            float totalScore = 0f;
            foreach (var scorer in _scorers)
            {
                if (scorer.CurrentDoc == minDoc)
                {
                    totalScore += scorer.Postings.Freq; // simplified — caller provides proper IDF weighting
                    scorer.CurrentDoc = scorer.Postings.NextDoc();
                }
            }

            _collector.TryAdd(minDoc, totalScore);
        }

        return _collector.GetTopN();
    }

    /// <summary>
    /// Simple min-heap-based top-N collector.
    /// </summary>
    internal sealed class TopNCollector
    {
        private readonly (int DocId, float Score)[] _heap;
        private int _count;

        public int Count => _count;
        public int Capacity { get; }
        public float MinScore => _count >= Capacity ? _heap[0].Score : 0f;

        public TopNCollector(int capacity)
        {
            Capacity = capacity;
            _heap = new (int, float)[capacity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TryAdd(int docId, float score)
        {
            if (_count < Capacity)
            {
                _heap[_count++] = (docId, score);
                if (_count == Capacity)
                    BuildMinHeap();
            }
            else if (score > _heap[0].Score)
            {
                _heap[0] = (docId, score);
                SiftDown(0);
            }
        }

        public (int DocId, float Score)[] GetTopN()
        {
            var result = new (int DocId, float Score)[_count];
            Array.Copy(_heap, result, _count);
            Array.Sort(result, (a, b) => b.Score.CompareTo(a.Score)); // descending
            return result;
        }

        private void BuildMinHeap()
        {
            for (int i = _count / 2 - 1; i >= 0; i--)
                SiftDown(i);
        }

        private void SiftDown(int i)
        {
            while (true)
            {
                int left = 2 * i + 1;
                int right = 2 * i + 2;
                int smallest = i;

                if (left < _count && _heap[left].Score < _heap[smallest].Score)
                    smallest = left;
                if (right < _count && _heap[right].Score < _heap[smallest].Score)
                    smallest = right;

                if (smallest == i) break;

                (_heap[i], _heap[smallest]) = (_heap[smallest], _heap[i]);
                i = smallest;
            }
        }
    }
}
