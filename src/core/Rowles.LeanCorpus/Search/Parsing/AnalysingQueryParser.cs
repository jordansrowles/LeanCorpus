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
