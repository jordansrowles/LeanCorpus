using System.Collections;

namespace Rowles.LeanCorpus.Search.Parsing;

/// <summary>Captures analysed tokens inline until a second token requires overflow storage.</summary>
internal sealed class QueryAnalysisTokenBuffer : IReadOnlyList<Analysis.Token>, Analysis.ISpanTokenSink
{
    private readonly QueryCompiler? _phraseOwner;
    private readonly int _sourceOffset;
    private Analysis.Token _first;
    private List<Analysis.Token>? _overflow;

    internal QueryAnalysisTokenBuffer(QueryCompiler? phraseOwner = null, int sourceOffset = 0)
    {
        _phraseOwner = phraseOwner;
        _sourceOffset = sourceOffset;
    }

    public int Count { get; private set; }

    internal bool UsesOverflowStorage => _overflow is not null;

    public Analysis.Token this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return index == 0 ? _first : _overflow![index];
        }
    }

    public void Add(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type = Analysis.Token.DefaultType,
        int positionIncrement = 1,
        byte[]? payload = null) =>
        Add(text, startOffset, endOffset, type, positionIncrement, positionLength: 1, payload);

    public void Add(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        int positionLength,
        byte[]? payload)
    {
        _phraseOwner?.ConsumeAnalysedPhraseToken(_sourceOffset);
        var token = new Analysis.Token(
            text.ToString(),
            startOffset,
            endOffset,
            type,
            positionIncrement,
            payload,
            positionLength);

        if (Count == 0)
        {
            _first = token;
        }
        else
        {
            if (_overflow is null)
            {
                _overflow = new List<Analysis.Token>(2) { _first };
            }

            _overflow.Add(token);
        }

        Count++;
    }

    public IEnumerator<Analysis.Token> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
            yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
