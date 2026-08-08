using System.Text;
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
    protected override string AnalyseMultiTerm(string term)
    {
        var builder = new StringBuilder(term.Length);
        int start = 0;
        for (int i = 0; i <= term.Length; i++)
        {
            if (i < term.Length && term[i] is not ('*' or '?'))
                continue;

            if (i > start)
            {
                string literal = term[start..i];
                string analysed = AnalyseTerm(literal);
                builder.Append(analysed.Length == 0 ? literal : analysed);
            }

            if (i < term.Length)
                builder.Append(term[i]);
            start = i + 1;
        }

        return builder.ToString();
    }

    /// <inheritdoc/>
    protected override string AnalyseRangeBound(string term)
    {
        string analysed = AnalyseTerm(term);
        return analysed.Length == 0 ? term : analysed;
    }
}
