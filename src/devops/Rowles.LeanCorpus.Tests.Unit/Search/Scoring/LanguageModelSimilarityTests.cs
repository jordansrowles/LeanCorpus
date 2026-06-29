using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Tests.Unit.Search.Scoring;

/// <summary>
/// Tests for the three language-model similarities, verifying correct scoring,
/// interface contract conformance, and default DIM dispatch.
/// </summary>
public sealed class LanguageModelSimilarityTests
{
    // ── DirichletSimilarity ────────────────────────────────────────────────────

    [Fact]
    public void Dirichlet_RequiresCollectionStatistics()
        => Assert.True(DirichletSimilarity.Instance.RequiresCollectionStatistics);

    [Fact]
    public void Dirichlet_ScoresTermWithCollectionModelInterpolation()
    {
        var sim = new DirichletSimilarity(mu: 1000f);
        // collectionProb = 100 / 10_000 = 0.01
        var (_, mu, cp) = sim.PrecomputeLmFactors(100, 50, 10f, 100, 10_000);
        Assert.Equal(0.01f, cp, 1e-6f);
        Assert.Equal(1000f, mu);

        // tf=5, docLength=100, mu=1000 → numerator=5+10=15, denom=1100 → log(15/1100)
        float score = sim.ScoreLmPrecomputed(0f, mu, cp, 5, 100);
        Assert.Equal(MathF.Log(15f / 1100f), score, 1e-6f);
    }

    [Fact]
    public void Dirichlet_ZeroCollectionProb_DoesNotExplode()
    {
        var sim = new DirichletSimilarity(mu: 2000f);
        float s = sim.ScoreLmPrecomputed(0f, 2000f, 0f, 1, 10);
        Assert.True(float.IsFinite(s));
    }

    // ── LMJelinekMercerSimilarity ──────────────────────────────────────────────

    [Fact]
    public void JelinekMercer_RequiresCollectionStatistics()
        => Assert.True(LMJelinekMercerSimilarity.Instance.RequiresCollectionStatistics);

    [Fact]
    public void JelinekMercer_InterpolatesDocumentAndCollectionModels()
    {
        var sim = new LMJelinekMercerSimilarity(0.2f);
        var (lambda, _, cp) = sim.PrecomputeLmFactors(50, 20, 8f, 200, 5_000);
        Assert.Equal(0.04f, cp, 1e-6f);
        Assert.Equal(0.2f, lambda);

        // tf=3, docLength=50 → docModel=0.06, interpolated=0.8*0.06+0.2*0.04=0.056
        float score = sim.ScoreLmPrecomputed(lambda, 0f, cp, 3, 50);
        Assert.Equal(MathF.Log(0.8f * 3f / 50f + 0.2f * 0.04f), score, 1e-6f);
    }

    [Fact]
    public void JelinekMercer_PureDocumentModel_WhenLambdaIsZero()
    {
        var sim = new LMJelinekMercerSimilarity(0f);
        float s = sim.ScoreLmPrecomputed(0f, 0f, 0.1f, 5, 100);
        Assert.Equal(MathF.Log(5f / 100f), s, 1e-6f);
    }

    // ── LMAbsoluteDiscountingSimilarity ────────────────────────────────────────

    [Fact]
    public void AbsoluteDiscounting_RequiresCollectionStatistics()
        => Assert.True(LMAbsoluteDiscountingSimilarity.Instance.RequiresCollectionStatistics);

    [Fact]
    public void AbsoluteDiscounting_DiscountsObservedCounts()
    {
        var sim = new LMAbsoluteDiscountingSimilarity(0.7f);
        var (delta, _, cp) = sim.PrecomputeLmFactors(100, 40, 12f, 500, 20_000);
        Assert.Equal(0.025f, cp, 1e-6f);
        Assert.Equal(0.7f, delta);

        // tf=4, docLength=200 → discounted=3.3, numer=3.3+3.5=6.8 → log(6.8/200)
        float score = sim.ScoreLmPrecomputed(delta, 0f, cp, 4, 200);
        float expected = MathF.Log((MathF.Max(4f - 0.7f, 0f) + 0.7f * 200f * 0.025f) / 200f);
        Assert.Equal(expected, score, 1e-6f);
    }

    [Fact]
    public void AbsoluteDiscounting_HandlesTfBelowDelta()
    {
        var sim = new LMAbsoluteDiscountingSimilarity(0.7f);
        float s = sim.ScoreLmPrecomputed(0.7f, 0f, 0.02f, 0, 50);
        // numerator = 0 + 0.7*50*0.02 = 0.7 → log(0.7/50)
        Assert.Equal(MathF.Log(0.7f * 50f * 0.02f / 50f), s, 1e-6f);
    }

    // ── ISimilarity default interface methods ──────────────────────────────────

    [Fact]
    public void Default_PrecomputeLmFactors_DelegatesToPrecomputeFactors()
    {
        ISimilarity bm25 = Bm25Similarity.Instance;
        var (f1, f2, f3) = bm25.PrecomputeLmFactors(200, 30, 15f, 5000, 1_000_000);
        var (ef1, ef2) = bm25.PrecomputeFactors(200, 30, 15f);
        Assert.Equal(ef1, f1);
        Assert.Equal(ef2, f2);
        Assert.Equal(0f, f3);
    }

    [Fact]
    public void Default_ScoreLmPrecomputed_DelegatesToScorePrecomputed()
    {
        ISimilarity bm25 = Bm25Similarity.Instance;
        var (f1, f2) = bm25.PrecomputeFactors(200, 30, 15f);
        float classic = bm25.ScorePrecomputed(f1, f2, 3, 50);
        float lm = bm25.ScoreLmPrecomputed(f1, f2, 0.1f, 3, 50);
        Assert.Equal(classic, lm, 1e-6f);
    }

    // ── Score fallback (without collection stats) ──────────────────────────────

    [Fact]
    public void Dirichlet_Score_ReturnsFiniteNonZero()
    {
        float s = DirichletSimilarity.Instance.Score(5, 100, 12f, 200, 30);
        Assert.True(float.IsFinite(s) && s != 0f);
    }

    [Fact]
    public void JelinekMercer_Score_ReturnsFiniteNonZero()
    {
        float s = LMJelinekMercerSimilarity.Instance.Score(5, 100, 12f, 200, 30);
        Assert.True(float.IsFinite(s) && s != 0f);
    }

    [Fact]
    public void AbsoluteDiscounting_Score_ReturnsFiniteNonZero()
    {
        float s = LMAbsoluteDiscountingSimilarity.Instance.Score(5, 100, 12f, 200, 30);
        Assert.True(float.IsFinite(s) && s != 0f);
    }
}
