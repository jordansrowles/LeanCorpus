using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search.Parsing;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class QueryParserSourceSpanTests
{
    [Fact(DisplayName = "Complex phrase: malformed alternative reports original UTF-16 source offset")]
    public void ComplexPhrase_MalformedAlternativeReportsOriginalOffset()
    {
        const string queryText = "title:\"😀 (quick fast)\"";
        var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser());

        QueryParseException exception = Assert.Throws<QueryParseException>(() => parser.Parse(queryText));

        Assert.Equal(queryText.IndexOf("fast", StringComparison.Ordinal), exception.Offset);
    }

    [Fact(DisplayName = "Complex phrase: analysed slot failure reports its original UTF-16 source offset")]
    public void ComplexPhrase_AnalysedSlotFailureReportsOriginalOffsetAndTokenContract()
    {
        const string queryText = "title:\"😀 (quick OR fast)\"";
        var analyser = new RecordingComplexPhraseAnalyser();
        var parser = new ComplexPhraseQueryParser("body", analyser);

        QueryParseException exception = Assert.Throws<QueryParseException>(() => parser.Parse(queryText));

        Assert.Equal(queryText.IndexOf("fast", StringComparison.Ordinal), exception.Offset);
        Assert.Collection(
            analyser.Tokens,
            token => Assert.Equal(new TokenSnapshot("😀", 0, 2, 1, 1), token),
            token => Assert.Equal(new TokenSnapshot("quick", 0, 5, 1, 1), token),
            token => Assert.Equal(new TokenSnapshot("fast", 0, 4, 1, 2), token));
    }

    [Fact(DisplayName = "Query parser: phrase graph failure reports its original UTF-16 source offset")]
    public void Parse_DisconnectedPhraseGraphReportsOriginalOffsetAndTokenContract()
    {
        const string queryText = "title:\"😀 graph\"";
        var analyser = new DisconnectedPhraseGraphAnalyser();
        var parser = new QueryParser("body", analyser);

        QueryParseException exception = Assert.Throws<QueryParseException>(() => parser.Parse(queryText));

        Assert.Equal(queryText.IndexOf('"'), exception.Offset);
        Assert.Contains("no complete path", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Collection(
            analyser.Tokens,
            token => Assert.Equal(new TokenSnapshot("😀", 0, 2, 1, 2), token),
            token => Assert.Equal(new TokenSnapshot("gr", 3, 5, 1, 5), token),
            token => Assert.Equal(new TokenSnapshot("aph", 5, 8, 2, 1), token));
    }

    [Fact(DisplayName = "Query parser: unexpected token message uses UTF-16 offsets")]
    public void Parse_UnexpectedTokenMessageUsesUtf16Offset()
    {
        const string queryText = "\"😀\" AND";
        var parser = new QueryParser("body", new StandardAnalyser());

        QueryParseException exception = Assert.Throws<QueryParseException>(() => parser.Parse(queryText));

        Assert.Equal(queryText.IndexOf("AND", StringComparison.Ordinal), exception.Offset);
        Assert.Contains($"UTF-16 offset {exception.Offset}", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("position 1", exception.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingComplexPhraseAnalyser : IAnalyser
    {
        public List<TokenSnapshot> Tokens { get; } = [];

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            string text = input.ToString();
            int positionLength = text.Equals("fast", StringComparison.Ordinal) ? 2 : 1;
            Emit(text, 0, text.Length, positionIncrement: 1, positionLength: positionLength, sink: sink);
        }

        private void Emit(string text, int startOffset, int endOffset, int positionIncrement, int positionLength, ISpanTokenSink sink)
        {
            sink.Add(text.AsSpan(), startOffset, endOffset, Token.DefaultType, positionIncrement, positionLength, payload: null);
            Tokens.Add(new TokenSnapshot(text, startOffset, endOffset, positionIncrement, positionLength));
        }
    }

    private sealed class DisconnectedPhraseGraphAnalyser : IAnalyser
    {
        public List<TokenSnapshot> Tokens { get; } = [];

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            Emit("😀", 0, 2, positionIncrement: 1, positionLength: 2, sink: sink);
            Emit("gr", 3, 5, positionIncrement: 1, positionLength: 5, sink: sink);
            Emit("aph", 5, 8, positionIncrement: 2, positionLength: 1, sink: sink);
        }

        private void Emit(string text, int startOffset, int endOffset, int positionIncrement, int positionLength, ISpanTokenSink sink)
        {
            sink.Add(text.AsSpan(), startOffset, endOffset, Token.DefaultType, positionIncrement, positionLength, payload: null);
            Tokens.Add(new TokenSnapshot(text, startOffset, endOffset, positionIncrement, positionLength));
        }
    }

    private readonly record struct TokenSnapshot(
        string Text,
        int StartOffset,
        int EndOffset,
        int PositionIncrement,
        int PositionLength);
}
