using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Codecs.Postings;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Block-Max WAND (Weak AND) scorer for top-K disjunctive query evaluation.
/// Uses per-block impact metadata (maxFreq, maxNorm) from skip entries
/// to skip entire 128-doc blocks whose maximum possible score contribution
/// falls below the current threshold (score of the Kth-best doc seen so far).
/// </summary>
internal sealed class BlockMaxWandScorer
{
    private readonly TermScorer[] _scorers;
    private readonly float _k1;
    private readonly float _b;
    private readonly float _avgDl;
    private int _blocksSkipped;
    private int _blocksScored;

    /// <summary>Number of blocks skipped due to WAND threshold.</summary>
    public int BlocksSkipped => _blocksSkipped;

    /// <summary>Number of blocks fully scored.</summary>
    public int BlocksScored => _blocksScored;

    public BlockMaxWandScorer(TermScorer[] scorers, float k1 = 1.2f, float b = 0.75f, float avgDl = 100f)
    {
        _scorers = scorers;
        _k1 = k1;
        _b = b;
        _avgDl = avgDl;
    }

    /// <summary>
    /// Executes Block-Max WAND, collecting results into <paramref name="collector"/>.
    /// Uses the collector's MinScore as the dynamic WAND threshold.
    /// </summary>
    public void ScoreInto(ref TopNCollector collector)
    {
        // Initialise all scorers to their first document.
        foreach (var scorer in _scorers)
        {
            scorer.CurrentDoc = scorer.Postings.NextDoc();
        }

        while (true)
        {
            // Find the document with the smallest ID among non-exhausted scorers.
            int minDoc = BlockPostingsEnum.NoMoreDocs;
            foreach (var scorer in _scorers)
            {
                if (scorer.CurrentDoc < minDoc)
                    minDoc = scorer.CurrentDoc;
            }

            if (minDoc == BlockPostingsEnum.NoMoreDocs)
                break;

            float threshold = collector.MinScore;

            // Compute the sum of block-max scores for the block containing minDoc.
            // If this sum cannot beat the threshold, skip the entire block.
            int blockIndex = minDoc / PackedIntCodec.BlockSize;
            float sumBlockMax = 0f;
            foreach (var scorer in _scorers)
            {
                if (scorer.CurrentDoc != BlockPostingsEnum.NoMoreDocs)
                    sumBlockMax += scorer.GetBlockMaxScore(blockIndex);
            }

            if (sumBlockMax <= threshold && collector.TotalHits >= collector.Capacity)
            {
                _blocksSkipped++;
                int nextBlockStart = (blockIndex + 1) * PackedIntCodec.BlockSize;
                foreach (var scorer in _scorers)
                {
                    if (scorer.CurrentDoc < nextBlockStart && scorer.CurrentDoc != BlockPostingsEnum.NoMoreDocs)
                    {
                        scorer.CurrentDoc = scorer.Postings.Advance(nextBlockStart);
                    }
                }
                continue;
            }

            _blocksScored++;

            // Score all terms present at minDoc using BM25.
            float totalScore = 0f;
            foreach (var scorer in _scorers)
            {
                if (scorer.CurrentDoc == minDoc)
                {
                    totalScore += scorer.ScoreCurrent();
                    scorer.CurrentDoc = scorer.Postings.NextDoc();
                }
            }

            collector.Collect(minDoc, totalScore);
        }
    }

    /// <summary>
    /// Convenience method that creates a temporary collector and returns results.
    /// Used by tests.
    /// </summary>
    public TopDocs Score(int topN)
    {
        var collector = new TopNCollector(topN);
        ScoreInto(ref collector);
        return collector.ToTopDocs();
    }

    /// <summary>
    /// A single term's postings list with precomputed per-block maximum BM25 scores.
    /// </summary>
    internal sealed class TermScorer
    {
        public BlockPostingsEnum Postings;
        public float[] BlockMaxScores; // one per skip entry (per 128-doc block)
        public float MaxScore;         // global max across all blocks
        public int CurrentDoc;

        private readonly float _idf;
        private readonly float _k1;
        private readonly float _b;
        private readonly float _avgDl;
        private readonly int[]? _fieldLengths;
        private readonly float[]? _fieldBoosts;

        /// <summary>
        /// Initialises a new <see cref="TermScorer"/>.
        /// </summary>
        /// <param name="postings">The postings list for this term.</param>
        /// <param name="idf">Precomputed IDF weight.</param>
        /// <param name="k1">BM25 k1 parameter.</param>
        /// <param name="b">BM25 b parameter.</param>
        /// <param name="avgDl">Average document length in the collection.</param>
        /// <param name="fieldLengths">Per-document field lengths, or null to assume average length.</param>
        /// <param name="fieldBoosts">Per-document field boosts, or null for no boosting.</param>
        public TermScorer(BlockPostingsEnum postings, float idf, float k1, float b, float avgDl,
            int[]? fieldLengths = null, float[]? fieldBoosts = null)
        {
            Postings = postings;
            _idf = idf;
            _k1 = k1;
            _b = b;
            _avgDl = avgDl;
            _fieldLengths = fieldLengths;
            _fieldBoosts = fieldBoosts;

            var skipEntries = postings.SkipEntries;
            BlockMaxScores = new float[skipEntries.Length];
            MaxScore = 0f;

            for (int i = 0; i < skipEntries.Length; i++)
            {
                ref readonly var skip = ref skipEntries[i];
                float maxFreq = skip.MaxFreqInBlock;
                float maxNorm = skip.MaxNormInBlock > 0
                    ? skip.MaxNormInBlock / 255f
                    : 1f;

                // The norm byte encodes doc-length quantisation. Use the
                // smallest length (largest norm → shortest doc) which gives the
                // highest possible BM25 score for the block.
                float dl = 1f / (maxNorm + float.Epsilon);
                float score = ComputeBM25(maxFreq, dl);
                BlockMaxScores[i] = score;
                if (score > MaxScore) MaxScore = score;
            }

            CurrentDoc = BlockPostingsEnum.NoMoreDocs;
        }

        /// <summary>
        /// Scores the document the postings enum is currently positioned on
        /// using full BM25.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ScoreCurrent()
        {
            int tf = Postings.Freq;
            int docId = Postings.DocId;
            float dl = _fieldLengths is not null && (uint)docId < (uint)_fieldLengths.Length
                ? _fieldLengths[docId]
                : _avgDl;

            float score = ComputeBM25(tf, dl);

            if (_fieldBoosts is not null && (uint)docId < (uint)_fieldBoosts.Length)
            {
                float boost = _fieldBoosts[docId];
                if (boost != 1.0f)
                    score *= boost;
            }

            return score;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float ComputeBM25(float tf, float dl)
        {
            return _idf * ((tf * (_k1 + 1f)) / (tf + _k1 * (1f - _b + _b * (dl / _avgDl))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetBlockMaxScore(int blockIndex)
        {
            if (blockIndex < 0 || blockIndex >= BlockMaxScores.Length)
                return MaxScore;
            return BlockMaxScores[blockIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetBlockIndex(int docId)
        {
            return docId / PackedIntCodec.BlockSize;
        }
    }
}