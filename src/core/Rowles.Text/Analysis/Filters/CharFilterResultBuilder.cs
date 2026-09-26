using System.Text;

namespace Rowles.LeanCorpus.Analysis.Filters;

internal sealed class CharFilterResultBuilder
{
    private readonly int _inputLength;
    private readonly StringBuilder _text;
    private List<int>? _sourceStartOffsets;
    private List<int>? _sourceEndOffsets;

    public CharFilterResultBuilder(int inputLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputLength);
        _inputLength = inputLength;
        _text = new StringBuilder(inputLength);
    }

    public void AppendUnchanged(ReadOnlySpan<char> text, int sourceStart)
    {
        ValidateSourceRange(sourceStart, text.Length);
        _text.Append(text);
        if (_sourceStartOffsets is null)
            return;

        for (int i = 0; i < text.Length; i++)
        {
            _sourceStartOffsets.Add(sourceStart + i);
            _sourceEndOffsets!.Add(sourceStart + i + 1);
        }
    }

    public void AppendReplacement(ReadOnlySpan<char> replacement, int sourceStart, int sourceLength)
    {
        ValidateSourceRange(sourceStart, sourceLength);
        if (replacement.Length != sourceLength && _sourceStartOffsets is null)
            InitialiseIdentityPrefix(replacement.Length);

        _text.Append(replacement);
        if (_sourceStartOffsets is null)
            return;

        for (int i = 0; i < replacement.Length; i++)
        {
            int start = sourceStart + (int)((long)i * sourceLength / replacement.Length);
            int end = sourceStart + (int)((long)(i + 1) * sourceLength / replacement.Length);
            _sourceStartOffsets.Add(start);
            _sourceEndOffsets!.Add(end);
        }
    }

    public CharFilterResult Build()
    {
        string text = _text.ToString();
        if (_sourceStartOffsets is null)
        {
            if (text.Length != _inputLength)
                throw new InvalidOperationException("An unchanged char-filter result must preserve the input length.");
            return new CharFilterResult(text, OffsetCorrectionMap.Identity(_inputLength));
        }

        if (_sourceStartOffsets.Count != text.Length || _sourceEndOffsets!.Count != text.Length)
            throw new InvalidOperationException("The char-filter correction map does not cover every output code unit.");

        return new CharFilterResult(
            text,
            OffsetCorrectionMap.CreateOwned(_inputLength, _sourceStartOffsets.ToArray(), _sourceEndOffsets.ToArray()));
    }

    private void InitialiseIdentityPrefix(int replacementLength)
    {
        int prefixLength = _text.Length;
        _sourceStartOffsets = new List<int>(prefixLength + replacementLength);
        _sourceEndOffsets = new List<int>(prefixLength + replacementLength);
        for (int i = 0; i < prefixLength; i++)
        {
            _sourceStartOffsets.Add(i);
            _sourceEndOffsets.Add(i + 1);
        }
    }

    private void ValidateSourceRange(int sourceStart, int sourceLength)
    {
        if (sourceStart < 0 || sourceLength < 0 || sourceStart > _inputLength - sourceLength)
            throw new ArgumentOutOfRangeException(nameof(sourceStart));
    }
}
