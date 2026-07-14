using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Codecs.Postings;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Block-Max WAND (Weak AND) scorer for top-K disjunctive query evaluation.
/// Uses per-block impact metadata (maxFreq, maxNorm) from skip entries
/// to skip entire 128-doc blocks whose maximum possible score contribution
/// falls below the current threshold.
/// </summary>
internal sealed class BlockMaxWandScorer
{
    private readonly TermScorer[] _scorers;
    private int _blocksSkipped;
    private int _blocksScored;

    public int BlocksSkipped => _blocksSkipped;
    public int BlocksScored => _blocksScored;

    public BlockMaxWandScorer(TermScorer[] scorers)
    {
        _scorers = scorers;
    }

    /// <summary>
    /// Executes Block-Max WAND, collecting results into <paramref name="collector"/>.
    /// </summary>
    /// <param name="collector">The collector to receive scored documents.</param>
    /// <param name="isLive">Optional predicate that returns <see langword="true"/> for live documents. Deleted documents are skipped.</param>
    public void ScoreInto(ref TopNCollector collector, Func<int, bool>? isLive = null)
    {
        foreach (var scorer in _scorers)
            scorer.CurrentDoc = scorer.Postings.NextDoc();

        while (true)
        {
            int minDoc = BlockPostingsEnum.NoMoreDocs;
            foreach (var scorer in _scorers)
            {
                if (scorer.CurrentDoc < minDoc)
                    minDoc = scorer.CurrentDoc;
            }

            if (minDoc == BlockPostingsEnum.NoMoreDocs)
                break;

            float threshold = collector.MinScore;

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
                        scorer.CurrentDoc = scorer.Postings.Advance(nextBlockStart);
                }
                continue;
            }

            _blocksScored++;

            float totalScore = 0f;
            foreach (var scorer in _scorers)
            {
                if (scorer.CurrentDoc == minDoc)
                {
                    totalScore += scorer.ScoreCurrent();
                    scorer.CurrentDoc = scorer.Postings.NextDoc();
                }
            }

            if (isLive is null || isLive(minDoc))
                collector.Collect(minDoc, totalScore);
        }
    }

    public TopDocs Score(int topN)
    {
        var collector = new TopNCollector(topN);
        ScoreInto(ref collector);
        return collector.ToTopDocs();
    }

    /// <summary>
    /// A single term's postings list with precomputed per-block maximum scores.
    /// </summary>
    internal sealed class TermScorer
    {
        public BlockPostingsEnum Postings;
        public float[] BlockMaxScores;
        public float MaxScore;
        public int CurrentDoc;

        private readonly ScoreFunc _scoreFunc;
        private readonly int[]? _fieldLengths;
        private readonly float[]? _fieldBoosts;
        private readonly float _avgDl;

        /// <summary>Scoring delegate: (tf, docLength) → score.</summary>
        public delegate float ScoreFunc(int tf, int docLength);

        /// <summary>
        /// Creates a BM25-backed term scorer. Used by tests.
        /// </summary>
        public TermScorer(BlockPostingsEnum postings, float idf, float k1, float b, float avgDl,
            int[]? fieldLengths = null, float[]? fieldBoosts = null)
        {
            Postings = postings;
            _fieldLengths = fieldLengths;
            _fieldBoosts = fieldBoosts;
            _avgDl = avgDl;

            float k1p1 = k1 + 1f;
            float k1Scale = k1 * (1f - b);
            float k1BOverAvgDl = k1 * b / avgDl;

            _scoreFunc = (tf, dl) =>
                idf * ((tf * k1p1) / (tf + k1Scale + k1BOverAvgDl * dl));

            var skipEntries = postings.SkipEntries;
            BlockMaxScores = new float[skipEntries.Length];
            MaxScore = 0f;

            for (int i = 0; i < skipEntries.Length; i++)
            {
                ref readonly var skip = ref skipEntries[i];
                float maxNorm = skip.MaxNormInBlock > 0 ? skip.MaxNormInBlock / 255f : 1f;
                float minDl = 1f / (maxNorm + float.Epsilon);
                float score = _scoreFunc((int)skip.MaxFreqInBlock, (int)minDl);
                BlockMaxScores[i] = score;
                if (score > MaxScore) MaxScore = score;
            }

            CurrentDoc = BlockPostingsEnum.NoMoreDocs;
            UpdateMaxScoreWithTail();
        }

        /// <summary>
        /// Creates an LM-backed term scorer. The <paramref name="lmScore"/> delegate
        /// should call <c>similarity.ScoreLmPrecomputed(f1, f2, f3, tf, docLength)</c>.
        /// </summary>
        public TermScorer(BlockPostingsEnum postings,
            float f1, float f2, float f3,
            Func<float, float, float, int, int, float> lmScore,
            float avgDl,
            int[]? fieldLengths = null, float[]? fieldBoosts = null)
        {
            Postings = postings;
            _fieldLengths = fieldLengths;
            _fieldBoosts = fieldBoosts;
            _avgDl = avgDl;

            _scoreFunc = (tf, dl) => lmScore(f1, f2, f3, tf, dl);

            var skipEntries = postings.SkipEntries;
            BlockMaxScores = new float[skipEntries.Length];
            MaxScore = 0f;

            for (int i = 0; i < skipEntries.Length; i++)
            {
                ref readonly var skip = ref skipEntries[i];
                // maxNorm quantises 1/(1+docLength) to 0-255. Largest norm
                // → shortest doc → highest LM score.
                // When no norm metadata is available, assume the shortest possible
                // document length so the bound remains safe for similarities whose
                // maximum score for a fixed term frequency occurs at minimum length
                // (BM25 and all built-in language models).
                float minDl = skip.MaxNormInBlock > 0
                    ? (255f / skip.MaxNormInBlock - 1f)
                    : 1f;
                if (minDl < 1f) minDl = 1f;
                float score = _scoreFunc((int)skip.MaxFreqInBlock, (int)minDl);
                BlockMaxScores[i] = score;
                if (score > MaxScore) MaxScore = score;
            }

            CurrentDoc = BlockPostingsEnum.NoMoreDocs;
            UpdateMaxScoreWithTail();
        }

        /// <summary>
        /// Scans the tail block (docs not covered by skip entries) to ensure
        /// <see cref="MaxScore"/> is a safe upper bound for the entire postings list.
        /// </summary>
        private void UpdateMaxScoreWithTail()
        {
            int skipCount = BlockMaxScores.Length;
            int tailStartIndex = skipCount * PackedIntCodec.BlockSize;
            if (tailStartIndex >= Postings.DocFreqCount)
                return;

            int targetDocId = skipCount > 0
                ? Postings.SkipEntries[skipCount - 1].LastDocId + 1
                : 0;

            Postings.Advance(targetDocId);
            while (Postings.DocId != BlockPostingsEnum.NoMoreDocs)
            {
                float score = ScoreCurrent();
                if (score > MaxScore)
                    MaxScore = score;
                Postings.NextDoc();
            }
            Postings.Reset();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ScoreCurrent()
        {
            int tf = Postings.Freq;
            int docId = Postings.DocId;
            int dl = _fieldLengths is not null && (uint)docId < (uint)_fieldLengths.Length
                ? _fieldLengths[docId]
                : (int)_avgDl;
            if (dl < 1) dl = 1;

            float score = _scoreFunc(tf, dl);

            if (_fieldBoosts is not null && (uint)docId < (uint)_fieldBoosts.Length)
            {
                float boost = _fieldBoosts[docId];
                if (Math.Abs(boost - 1.0f) > 1e-6f) score *= boost;
            }

            return score;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetBlockMaxScore(int blockIndex)
        {
            if (blockIndex < 0 || blockIndex >= BlockMaxScores.Length)
                return MaxScore;
            return BlockMaxScores[blockIndex];
        }
    }
}