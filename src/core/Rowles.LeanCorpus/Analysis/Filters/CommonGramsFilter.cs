using System.Buffers;
using System.Collections.Frozen;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Produces bigrams of consecutive common words to improve phrase query recall.
/// </summary>
/// <remarks>
/// <para>When two consecutive tokens are both in the <em>common words</em> set, this
/// filter emits a bigram token (e.g. <c>"the_quick"</c>) in addition to the individual
/// common words. The bigram appears at the same position as the first common word
/// (<c>positionIncrement = 0</c>), enabling phrase queries to match across common-word
/// boundaries.</para>
/// <para>This filter is stateful — it buffers the previous token across calls — and
/// relies on <see cref="ISpanTokenFilter.Finish"/> to flush the final buffered token
/// at end-of-stream.</para>
/// <para>Common-word lookup uses <see cref="FrozenSet{T}"/> with
/// <c>AlternateLookup&lt;ReadOnlySpan&lt;char&gt;&gt;</c> for zero-allocation
/// membership tests, matching the pattern used by <see cref="StopWordFilter"/>.</para>
/// </remarks>
public sealed class CommonGramsFilter : ISpanTokenFilter
{
    private readonly FrozenSet<string> _commonWords;
    private readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly string _separator;

    // Mutable state — one token buffered across Apply calls.
    private string _previousText = string.Empty;
    private int _previousStartOffset;
    private int _previousEndOffset;
    private string _previousType = Token.DefaultType;
    private int _previousPositionIncrement;
    private byte[]? _previousPayload;
    private bool _previousIsCommon;
    private bool _hasPrevious;

    /// <summary>
    /// Initialises a new <see cref="CommonGramsFilter"/>.
    /// </summary>
    /// <param name="commonWords">The set of words considered common.
    /// Comparisons are case-insensitive (ordinal ignore case).</param>
    /// <param name="separator">The separator inserted between combined token texts
    /// in the bigram. Defaults to <c>"_"</c>.</param>
    public CommonGramsFilter(IEnumerable<string> commonWords, string separator = "_")
    {
        ArgumentNullException.ThrowIfNull(commonWords);
        ArgumentNullException.ThrowIfNull(separator);

        _commonWords = commonWords.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _lookup = _commonWords.GetAlternateLookup<ReadOnlySpan<char>>();
        _separator = separator;
    }

    /// <inheritdoc/>
    public void Apply(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        byte[]? payload,
        ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        bool currentIsCommon = _lookup.Contains(text);

        if (_hasPrevious)
        {
            if (_previousIsCommon && currentIsCommon)
            {
                // Emit the bigram at the same position as the first common word.
                EmitBigram(_previousText.AsSpan(), text, _previousStartOffset, endOffset,
                    _previousType, _previousPayload, sink);

                // Then emit the first common word with its original position increment.
                sink.Add(
                    _previousText.AsSpan(),
                    _previousStartOffset,
                    _previousEndOffset,
                    _previousType,
                    _previousPositionIncrement,
                    _previousPayload);
            }
            else
            {
                // Emit the buffered previous token unchanged.
                sink.Add(
                    _previousText.AsSpan(),
                    _previousStartOffset,
                    _previousEndOffset,
                    _previousType,
                    _previousPositionIncrement,
                    _previousPayload);
            }
        }

        // Buffer the current token for the next Apply call.
        _previousText = text.ToString(); // must materialise — span is transient
        _previousStartOffset = startOffset;
        _previousEndOffset = endOffset;
        _previousType = type;
        _previousPositionIncrement = positionIncrement;
        _previousPayload = payload;
        _previousIsCommon = currentIsCommon;
        _hasPrevious = true;
    }

    /// <inheritdoc/>
    public void Finish(ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_hasPrevious)
        {
            sink.Add(
                _previousText.AsSpan(),
                _previousStartOffset,
                _previousEndOffset,
                _previousType,
                _previousPositionIncrement,
                _previousPayload);

            _hasPrevious = false;
        }
    }

    /// <summary>
    /// Builds a bigram string from two spans and sends it to the sink with
    /// <c>positionIncrement = 0</c> (same position as the first word).
    /// </summary>
    private void EmitBigram(
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        int startOffset,
        int endOffset,
        string type,
        byte[]? payload,
        ISpanTokenSink sink)
    {
        int totalLength = first.Length + _separator.Length + second.Length;

        const int StackThreshold = 128;
        char[]? rented = null;
        try
        {
            Span<char> buffer = totalLength <= StackThreshold
                ? stackalloc char[totalLength]
                : (rented = ArrayPool<char>.Shared.Rent(totalLength)).AsSpan(0, totalLength);

            first.CopyTo(buffer);
            _separator.AsSpan().CopyTo(buffer[first.Length..]);
            second.CopyTo(buffer[(first.Length + _separator.Length)..]);

            sink.Add(buffer, startOffset, endOffset, type, positionIncrement: 0, payload);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }
}
