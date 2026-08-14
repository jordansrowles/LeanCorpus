using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Tests.Metadata;
using Xunit;

namespace Rowles.Text.Tests;

[Category(TestCategory.Unit)]
[Area(TestArea.Analysers)]
[Area(TestArea.Tokenisers)]
public sealed class StandalonePackageTests
{
    [Fact]
    public void StandardAnalyser_UsesStandaloneAssembly()
    {
        var analyser = new StandardAnalyser();
        var sink = new MaterialisingSink();

        analyser.Analyse("The QUICK fox", sink);

        Assert.Equal(["quick", "fox"], sink.Tokens);
    }

    [Fact]
    public void Standalone_assembly_has_no_LeanCorpus_reference()
    {
        Assert.DoesNotContain(
            typeof(StandardAnalyser).Assembly.GetReferencedAssemblies(),
            static assembly => assembly.Name == "Rowles.LeanCorpus");
    }

    [Fact]
    public void JapaneseTokeniser_ReadsStandaloneCodec()
    {
        string dictionaryPath = Path.Combine(AppContext.BaseDirectory, "lexicons", "japanese.jlc");
        using var tokeniser = new JapaneseTokeniser(dictionaryPath);
        var sink = new MaterialisingSink();

        tokeniser.Tokenise("私は学生です", sink);

        Assert.Equal(["私", "は", "学生", "です"], sink.Tokens);
    }

    private sealed class MaterialisingSink : ISpanTokenSink
    {
        public List<string> Tokens { get; } = [];

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            Tokens.Add(text.ToString());
        }
    }
}
