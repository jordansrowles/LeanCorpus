using System.Text;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.Text.Tests.Stemmers;

/// <summary>
/// Pins the 4.0 normalisation contract at the Slovak analyser boundary: combining marks stay inside
/// words, and analysis does not normalise, so text must be NFC (as the stemmer expects) for
/// decomposed and precomposed spellings to share terms.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Stemmers)]
public sealed class SlovakAnalyserBoundaryTests
{
    [Theory(DisplayName = "Slovak analyser: decomposed input stays one word and is not normalised")]
    [InlineData("škôl")]
    [InlineData("kosťou")]
    [InlineData("príčinách")]
    [InlineData("Veľké")]
    public void DecomposedInput_StaysOneWord_AndIsNotNormalised(string precomposed)
    {
        string decomposed = precomposed.Normalize(NormalizationForm.FormD);
        Assert.NotEqual(precomposed, decomposed);

        var nfc = Analyse(precomposed);
        var nfd = Analyse(decomposed);

        var token = Assert.Single(nfd);
        Assert.Single(nfc);
        Assert.Equal(0, token.StartOffset);
        Assert.Equal(decomposed.Length, token.EndOffset);
        Assert.NotEqual(nfc[0].Text, token.Text);
        Assert.Equal(nfc[0].Text, Analyse(decomposed.Normalize(NormalizationForm.FormC))[0].Text);
    }

    private static List<Token> Analyse(string text)
    {
        var sink = new MaterialisingTokenSink();
        AnalyserFactory.Create("sk").Analyse(text, sink);
        return sink.Tokens;
    }
}
