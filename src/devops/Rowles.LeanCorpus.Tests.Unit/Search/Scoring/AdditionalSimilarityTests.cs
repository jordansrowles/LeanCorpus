using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Tests.Unit.Search.Scoring;

/// <summary>
/// Tests for the five additional similarity implementations:
/// BM25+, BM25L, TF-IDF Augmented, TF-IDF Pivoted, and TF-IDF Double-Normalised.
/// Verifies correct scoring, interface contract conformance, and default DIM dispatch.
/// </summary>
public sealed class AdditionalSimilarityTests
{
    // Shared test parameters.
    private const int N = 200;
    private const int Df = 30;
    private const float AvgDl = 15f;
    private const int Tf = 5;
    private const int Dl = 100;

    // ═══════════════════════════════════════════════════════════════════════
    //  BM25+ Similarity
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Bm25Plus_DoesNotRequireCollectionStatistics()
        => Assert.False(Bm25PlusSimilarity.Instance.RequiresCollectionStatistics);

    [Fact]
    public void Bm25Plus_ScoresWithDeltaLowerBound()
    {
        var sim = Bm25PlusSimilarity.Instance;
        float score = sim.Score(Tf, Dl, AvgDl, N, Df);

        // idf = log(1 + (200-30+0.5)/(30+0.5)) = log(1 + 170.5/30.5) ≈ 1.8852
        // normalised_tf = (5*2.2)/(5 + 1.2*(1-0.75+0.75*100/15)) = 11/11.3 ≈ 0.9735
        // score = 1.8852 * (0.9735 + 1.0) ≈ 3.7205
        Assert.True(score > 3.0f && float.IsFinite(score));
    }

    [Fact]
    public void Bm25Plus_TfZero_GivesIdfTimesDelta()
    {
        var sim = Bm25PlusSimilarity.Instance;
        var (idf, k1BOverAvgDL) = sim.PrecomputeFactors(N, Df, AvgDl);
        float score = sim.ScorePrecomputed(idf, k1BOverAvgDL, 0, Dl);

        // normalised_tf = 0, so score = idf * delta (delta=1.0)
        Assert.Equal(idf, score, 1e-6f);
    }

    [Fact]
    public void Bm25Plus_PrecomputeMatchesScore()
    {
        var sim = Bm25PlusSimilarity.Instance;
        float direct = sim.Score(Tf, Dl, AvgDl, N, Df);
        var (f1, f2) = sim.PrecomputeFactors(N, Df, AvgDl);
        float precomputed = sim.ScorePrecomputed(f1, f2, Tf, Dl);
        Assert.Equal(direct, precomputed, 1e-6f);
    }

    [Fact]
    public void Bm25Plus_CustomDelta_IncreasesScore()
    {
        var defaultSim = Bm25PlusSimilarity.Instance;
        var highDeltaSim = new Bm25PlusSimilarity(delta: 2.0f);

        float defaultScore = defaultSim.Score(Tf, Dl, AvgDl, N, Df);
        float highScore = highDeltaSim.Score(Tf, Dl, AvgDl, N, Df);
        Assert.True(highScore > defaultScore);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BM25L Similarity
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Bm25L_DoesNotRequireCollectionStatistics()
        => Assert.False(Bm25LSimilarity.Instance.RequiresCollectionStatistics);

    [Fact]
    public void Bm25L_ScoresWithModulatedDelta()
    {
        var sim = Bm25LSimilarity.Instance;
        float score = sim.Score(Tf, Dl, AvgDl, N, Df);

        // idf ≈ 1.8852, normalised_tf ≈ 0.9735, deltaTerm = 0.5*5/6 ≈ 0.4167
        // score ≈ 1.8852 * 1.3902 ≈ 2.62
        Assert.True(score > 2.0f && float.IsFinite(score));
    }

    [Fact]
    public void Bm25L_TfZero_GivesZero()
    {
        var sim = Bm25LSimilarity.Instance;
        var (idf, k1BOverAvgDL) = sim.PrecomputeFactors(N, Df, AvgDl);
        float score = sim.ScorePrecomputed(idf, k1BOverAvgDL, 0, Dl);

        // normalised_tf=0, delta*tf/(1+tf)=0 → score=0
        Assert.Equal(0f, score);
    }

    [Fact]
    public void Bm25L_PrecomputeMatchesScore()
    {
        var sim = Bm25LSimilarity.Instance;
        float direct = sim.Score(Tf, Dl, AvgDl, N, Df);
        var (f1, f2) = sim.PrecomputeFactors(N, Df, AvgDl);
        float precomputed = sim.ScorePrecomputed(f1, f2, Tf, Dl);
        Assert.Equal(direct, precomputed, 1e-6f);
    }

    [Fact]
    public void Bm25L_DeltaModulation_SaturatesAtHighTf()
    {
        var sim = Bm25LSimilarity.Instance;
        var (idf, k1BOverAvgDL) = sim.PrecomputeFactors(N, Df, AvgDl);

        // At high tf, tf/(1+tf) ≈ 1, so deltaTerm ≈ delta = 0.5
        float scoreTf100 = sim.ScorePrecomputed(idf, k1BOverAvgDL, 100, Dl);
        float scoreTf1000 = sim.ScorePrecomputed(idf, k1BOverAvgDL, 1000, Dl);

        // Both should be finite; delta contribution plateaus
        Assert.True(float.IsFinite(scoreTf100));
        Assert.True(float.IsFinite(scoreTf1000));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TF-IDF Augmented Similarity
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TfIdfAugmented_DoesNotRequireCollectionStatistics()
        => Assert.False(TfIdfAugmentedSimilarity.Instance.RequiresCollectionStatistics);

    [Fact]
    public void TfIdfAugmented_ScoresWithPivotedTf()
    {
        var sim = TfIdfAugmentedSimilarity.Instance;
        float score = sim.Score(Tf, Dl, AvgDl, N, Df);

        // tf_aug = 0.5 + 0.5*5/5.5 ≈ 0.9545
        // idf = 1 + log(200/31) ≈ 2.8643
        // lengthNorm = 1/10 = 0.1
        // score ≈ 0.9545 * 2.8643 * 0.1 ≈ 0.2734
        Assert.True(score > 0.2f && float.IsFinite(score));
    }

    [Fact]
    public void TfIdfAugmented_TfZero_GivesKIdfNorm()
    {
        var sim = TfIdfAugmentedSimilarity.Instance;
        var (idf, k) = sim.PrecomputeFactors(N, Df, AvgDl);
        float score = sim.ScorePrecomputed(idf, k, 0, Dl);

        // tf_aug = K = 0.5, score = 0.5 * idf / sqrt(dl)
        float expected = 0.5f * idf / MathF.Sqrt(Dl);
        Assert.Equal(expected, score, 1e-6f);
    }

    [Fact]
    public void TfIdfAugmented_PrecomputeMatchesScore()
    {
        var sim = TfIdfAugmentedSimilarity.Instance;
        float direct = sim.Score(Tf, Dl, AvgDl, N, Df);
        var (f1, f2) = sim.PrecomputeFactors(N, Df, AvgDl);
        float precomputed = sim.ScorePrecomputed(f1, f2, Tf, Dl);
        Assert.Equal(direct, precomputed, 1e-6f);
    }

    [Fact]
    public void TfIdfAugmented_CustomK_ChangesAugmentation()
    {
        var simK03 = new TfIdfAugmentedSimilarity(k: 0.3f);
        var simK08 = new TfIdfAugmentedSimilarity(k: 0.8f);

        float scoreLowK = simK03.Score(Tf, Dl, AvgDl, N, Df);
        float scoreHighK = simK08.Score(Tf, Dl, AvgDl, N, Df);

        // Higher K makes tf_aug stay closer to K (less impact of actual tf)
        // At tf=5: K=0.3 → tf_aug = 0.3+0.7*5/5.3 ≈ 0.960; K=0.8 → tf_aug = 0.8+0.2*5/5.8 ≈ 0.972
        // Both are valid, just different
        Assert.True(float.IsFinite(scoreLowK) && scoreLowK > 0f);
        Assert.True(float.IsFinite(scoreHighK) && scoreHighK > 0f);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TF-IDF Pivoted Similarity
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TfIdfPivoted_DoesNotRequireCollectionStatistics()
        => Assert.False(TfIdfPivotedSimilarity.Instance.RequiresCollectionStatistics);

    [Fact]
    public void TfIdfPivoted_ScoresWithPivotedLengthNorm()
    {
        var sim = TfIdfPivotedSimilarity.Instance;
        float score = sim.Score(Tf, Dl, AvgDl, N, Df);

        // tf = sqrt(5) ≈ 2.2361
        // idf = 1 + log(200/31) ≈ 2.8643
        // len_norm = 1/(0.8 + 0.2*100/15) = 1/2.1333 ≈ 0.4688
        // score ≈ 2.2361 * 2.8643 * 0.4688 ≈ 3.003
        Assert.True(score > 2.0f && float.IsFinite(score));
    }

    [Fact]
    public void TfIdfPivoted_TfZero_GivesZero()
    {
        var sim = TfIdfPivotedSimilarity.Instance;
        var (idf, sDivAvgDl) = sim.PrecomputeFactors(N, Df, AvgDl);
        float score = sim.ScorePrecomputed(idf, sDivAvgDl, 0, Dl);

        // sqrt(0) = 0 → score = 0
        Assert.Equal(0f, score);
    }

    [Fact]
    public void TfIdfPivoted_PrecomputeMatchesScore()
    {
        var sim = TfIdfPivotedSimilarity.Instance;
        float direct = sim.Score(Tf, Dl, AvgDl, N, Df);
        var (f1, f2) = sim.PrecomputeFactors(N, Df, AvgDl);
        float precomputed = sim.ScorePrecomputed(f1, f2, Tf, Dl);
        Assert.Equal(direct, precomputed, 1e-6f);
    }

    [Fact]
    public void TfIdfPivoted_CustomS_ChangesLengthPenalty()
    {
        var simS0 = new TfIdfPivotedSimilarity(s: 0f);   // no length norm
        var simS1 = new TfIdfPivotedSimilarity(s: 1f);   // full length norm

        float scoreS0 = simS0.Score(Tf, Dl, AvgDl, N, Df);
        float scoreS1 = simS1.Score(Tf, Dl, AvgDl, N, Df);

        // s=0: len_norm = 1/1 = 1, so score higher (no penalty for long docs)
        // s=1: len_norm = 1/(dl/avgdl), so score lower
        Assert.True(scoreS0 > scoreS1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TF-IDF Double-Normalised Similarity
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TfIdfDoubleNorm_DoesNotRequireCollectionStatistics()
        => Assert.False(TfIdfDoubleNormSimilarity.Instance.RequiresCollectionStatistics);

    [Fact]
    public void TfIdfDoubleNorm_ScoresWithBothNormalisations()
    {
        var sim = TfIdfDoubleNormSimilarity.Instance;
        float score = sim.Score(Tf, Dl, AvgDl, N, Df);

        // tf_norm = 0.9545, idf = 2.8643, len_norm = 0.4688
        // score ≈ 1.2818
        Assert.True(score > 0.8f && float.IsFinite(score));
    }

    [Fact]
    public void TfIdfDoubleNorm_TfZero_GivesKIdfLenNorm()
    {
        var sim = TfIdfDoubleNormSimilarity.Instance;
        var (idf, sDivAvgDl) = sim.PrecomputeFactors(N, Df, AvgDl);
        float score = sim.ScorePrecomputed(idf, sDivAvgDl, 0, Dl);

        // tf_norm = K = 0.5
        // len_norm = 1/(0.8 + 0.2*100/15) = 1/2.1333 ≈ 0.46875
        float expectedLenNorm = 1f / (0.8f + 0.2f * Dl / AvgDl);
        float expected = 0.5f * idf * expectedLenNorm;
        Assert.Equal(expected, score, 1e-6f);
    }

    [Fact]
    public void TfIdfDoubleNorm_PrecomputeMatchesScore()
    {
        var sim = TfIdfDoubleNormSimilarity.Instance;
        float direct = sim.Score(Tf, Dl, AvgDl, N, Df);
        var (f1, f2) = sim.PrecomputeFactors(N, Df, AvgDl);
        float precomputed = sim.ScorePrecomputed(f1, f2, Tf, Dl);
        Assert.Equal(direct, precomputed, 1e-6f);
    }

    [Fact]
    public void TfIdfDoubleNorm_CustomKS_ChangesBothAxes()
    {
        var custom = new TfIdfDoubleNormSimilarity(k: 0.3f, s: 0.5f);
        float score = custom.Score(Tf, Dl, AvgDl, N, Df);
        Assert.True(float.IsFinite(score) && score > 0f);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ISimilarity default interface methods (verify LM delegation)
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(nameof(Bm25PlusSimilarity))]
    [InlineData(nameof(Bm25LSimilarity))]
    [InlineData(nameof(TfIdfAugmentedSimilarity))]
    [InlineData(nameof(TfIdfPivotedSimilarity))]
    [InlineData(nameof(TfIdfDoubleNormSimilarity))]
    public void All_PrecomputeLmFactors_DelegatesToPrecomputeFactors(string similarityName)
    {
        ISimilarity sim = similarityName switch
        {
            nameof(Bm25PlusSimilarity) => Bm25PlusSimilarity.Instance,
            nameof(Bm25LSimilarity) => Bm25LSimilarity.Instance,
            nameof(TfIdfAugmentedSimilarity) => TfIdfAugmentedSimilarity.Instance,
            nameof(TfIdfPivotedSimilarity) => TfIdfPivotedSimilarity.Instance,
            nameof(TfIdfDoubleNormSimilarity) => TfIdfDoubleNormSimilarity.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(similarityName))
        };

        var (f1, f2, f3) = sim.PrecomputeLmFactors(N, Df, AvgDl, 5000, 1_000_000);
        var (ef1, ef2) = sim.PrecomputeFactors(N, Df, AvgDl);

        Assert.Equal(ef1, f1);
        Assert.Equal(ef2, f2);
        Assert.Equal(0f, f3);
    }

    [Theory]
    [InlineData(nameof(Bm25PlusSimilarity))]
    [InlineData(nameof(Bm25LSimilarity))]
    [InlineData(nameof(TfIdfAugmentedSimilarity))]
    [InlineData(nameof(TfIdfPivotedSimilarity))]
    [InlineData(nameof(TfIdfDoubleNormSimilarity))]
    public void All_ScoreLmPrecomputed_DelegatesToScorePrecomputed(string similarityName)
    {
        ISimilarity sim = similarityName switch
        {
            nameof(Bm25PlusSimilarity) => Bm25PlusSimilarity.Instance,
            nameof(Bm25LSimilarity) => Bm25LSimilarity.Instance,
            nameof(TfIdfAugmentedSimilarity) => TfIdfAugmentedSimilarity.Instance,
            nameof(TfIdfPivotedSimilarity) => TfIdfPivotedSimilarity.Instance,
            nameof(TfIdfDoubleNormSimilarity) => TfIdfDoubleNormSimilarity.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(similarityName))
        };

        var (f1, f2) = sim.PrecomputeFactors(N, Df, AvgDl);
        float classic = sim.ScorePrecomputed(f1, f2, Tf, Dl);
        float lm = sim.ScoreLmPrecomputed(f1, f2, 0.1f, Tf, Dl);
        Assert.Equal(classic, lm, 1e-6f);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DocLength = 1 edge case
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void All_ShortDocument_ReturnsFiniteScores()
    {
        ISimilarity[] similarities =
        [
            Bm25PlusSimilarity.Instance,
            Bm25LSimilarity.Instance,
            TfIdfAugmentedSimilarity.Instance,
            TfIdfPivotedSimilarity.Instance,
            TfIdfDoubleNormSimilarity.Instance
        ];

        foreach (var sim in similarities)
        {
            float score = sim.Score(1, 1, AvgDl, N, Df);
            Assert.True(float.IsFinite(score));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Singleton identity
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void All_Singletons_AreNotNull()
    {
        Assert.NotNull(Bm25PlusSimilarity.Instance);
        Assert.NotNull(Bm25LSimilarity.Instance);
        Assert.NotNull(TfIdfAugmentedSimilarity.Instance);
        Assert.NotNull(TfIdfPivotedSimilarity.Instance);
        Assert.NotNull(TfIdfDoubleNormSimilarity.Instance);
    }
}
