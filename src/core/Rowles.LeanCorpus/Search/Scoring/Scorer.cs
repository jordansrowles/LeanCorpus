namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Scores candidate documents produced by a custom query weight.</summary>
public abstract class Scorer
{
    /// <summary>Gets the current global document identifier during collection.</summary>
    public virtual int DocId => -1;

    /// <summary>Gets the current score during collection.</summary>
    public virtual float Score() => 0.0f;

    /// <summary>Scores one candidate document.</summary>
    /// <param name="docId">The global document identifier.</param>
    /// <param name="approximationScore">The score produced by the approximation query.</param>
    public abstract float Score(int docId, float approximationScore);

    /// <summary>Receives the current minimum competitive score when available.</summary>
    public virtual void SetMinCompetitiveScore(float minCompetitiveScore)
    {
    }
}
