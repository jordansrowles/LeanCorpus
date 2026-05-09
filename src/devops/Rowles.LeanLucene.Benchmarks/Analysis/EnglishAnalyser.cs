using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Analysis.Filters;
using Rowles.LeanLucene.Analysis.Tokenisers;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// English analysis pipeline with stemming, used as a benchmark baseline.
/// Pipeline: tokenise -> lowercase -> stop-word removal -> Porter stem.
/// </summary>
/// <remarks>
/// This pipeline reduces tokens to their root forms (e.g. "running", "runs", "ran"
/// all become "run"), giving improved recall at the cost of ~2.3x higher CPU and
/// ~15.6x more allocated memory than <see cref="StandardAnalyser"/>. It is kept here
/// alongside the benchmarks that measure that trade-off, not in the library.
///
/// Thread-safety: each instance is not thread-safe. Create one instance per thread.
/// </remarks>
internal sealed class EnglishAnalyser : IAnalyser
{
    private readonly Analyser _pipeline = new(
        new Tokeniser(),
        new LowercaseFilter(),
        new StopWordFilter(),
        new PorterStemmerFilter());

    /// <inheritdoc/>
    public List<Token> Analyse(ReadOnlySpan<char> input) => _pipeline.Analyse(input);
}
