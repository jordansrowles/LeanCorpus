using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Search.Parsing;

/// <summary>Query parser that also analyses literal portions of wildcard and prefix terms.</summary>
public sealed class AnalysingQueryParser : QueryParser
{
    /// <summary>Initialises an analysing query parser.</summary>
    public AnalysingQueryParser(string defaultField, IAnalyser analyser, bool lenient = false)
        : base(defaultField, analyser, lenient)
    {
    }

    /// <summary>Initialises an analysing parser with a per-parse analyser factory.</summary>
    /// <param name="defaultField">The field used when no explicit field prefix is present.</param>
    /// <param name="analyserFactory">Creates an analyser for each parse invocation.</param>
    /// <param name="lenient">Whether syntax errors return the best-effort parsed query.</param>
    public AnalysingQueryParser(string defaultField, Func<IAnalyser> analyserFactory, bool lenient = false)
        : base(defaultField, analyserFactory, lenient)
    {
    }

    /// <inheritdoc/>
    protected override string AnalyseMultiTerm(string term) =>
        NormaliseMultiTermPattern(term, literal =>
        {
            string analysed = AnalyseSingleToken(literal);
            return analysed.Length == 0 ? literal : analysed;
        });

    /// <inheritdoc/>
    protected override string AnalyseRangeBound(string term)
    {
        string analysed = AnalyseSingleToken(term);
        return analysed.Length == 0 ? term : analysed;
    }
}
