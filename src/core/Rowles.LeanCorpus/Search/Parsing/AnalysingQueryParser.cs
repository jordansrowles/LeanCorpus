using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Search.Parsing;

/// <summary>Query parser that normalises literal portions of wildcard, prefix and range terms.</summary>
public sealed class AnalysingQueryParser : QueryParser
{
    /// <summary>Initialises an analysing query parser.</summary>
    /// <param name="defaultField">The field used when no explicit field prefix is present.</param>
    /// <param name="analyser">The analyser used to normalise literal query text.</param>
    /// <param name="lenient">Must be <see langword="false"/>; lenient recovery was removed.</param>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="lenient"/> is <see langword="true"/>.</exception>
    public AnalysingQueryParser(string defaultField, IAnalyser analyser, bool lenient = false)
        : base(defaultField, analyser, lenient)
    {
    }

    /// <summary>Initialises an analysing query parser with explicit parser-time budgets.</summary>
    /// <param name="defaultField">The field used when no explicit field prefix is present.</param>
    /// <param name="analyser">The analyser used to normalise literal query text.</param>
    /// <param name="options">The parser-time limits to enforce for each query.</param>
    public AnalysingQueryParser(string defaultField, IAnalyser analyser, QueryParserOptions options)
        : base(defaultField, analyser, options)
    {
    }

    /// <summary>Initialises an analysing parser with a per-parse analyser factory.</summary>
    /// <param name="defaultField">The field used when no explicit field prefix is present.</param>
    /// <param name="analyserFactory">Creates an analyser for each parse invocation.</param>
    /// <param name="lenient">Must be <see langword="false"/>; lenient recovery was removed.</param>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="lenient"/> is <see langword="true"/>.</exception>
    public AnalysingQueryParser(string defaultField, Func<IAnalyser> analyserFactory, bool lenient = false)
        : base(defaultField, analyserFactory, lenient)
    {
    }

    /// <summary>Initialises an analysing parser with explicit budgets and a per-parse analyser factory.</summary>
    /// <param name="defaultField">The field used when no explicit field prefix is present.</param>
    /// <param name="analyserFactory">Creates an analyser for each parse invocation.</param>
    /// <param name="options">The parser-time limits to enforce for each query.</param>
    public AnalysingQueryParser(string defaultField, Func<IAnalyser> analyserFactory, QueryParserOptions options)
        : base(defaultField, analyserFactory, options)
    {
    }

    /// <inheritdoc/>
    protected override string AnalyseMultiTerm(string term) =>
        NormaliseMultiTermPattern(term, literal => NormaliseSingleTerm(Analyser, literal));

    /// <inheritdoc/>
    protected override string AnalyseRangeBound(string term) => NormaliseSingleTerm(Analyser, term);
}
