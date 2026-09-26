namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Maps UTF-16 offsets in transformed text back to UTF-16 offsets in the input text.
/// </summary>
/// <remarks>
/// The map stores one source range per transformed UTF-16 code unit. A zero-length source range represents
/// inserted text. Deleted input ranges have no transformed code unit: start offsets use the source position
/// of the following transformed character, while end offsets use the source position of the preceding one.
/// This preserves the source bounds of tokens on either side of a deletion.
/// </remarks>
public sealed class OffsetCorrectionMap
{
    private readonly int[]? _sourceStartOffsets;
    private readonly int[]? _sourceEndOffsets;

    private OffsetCorrectionMap(int inputLength, int outputLength, int[]? sourceStartOffsets, int[]? sourceEndOffsets)
    {
        InputLength = inputLength;
        OutputLength = outputLength;
        _sourceStartOffsets = sourceStartOffsets;
        _sourceEndOffsets = sourceEndOffsets;
    }

    /// <summary>Gets the input length, in UTF-16 code units.</summary>
    public int InputLength { get; }

    /// <summary>Gets the transformed output length, in UTF-16 code units.</summary>
    public int OutputLength { get; }

    /// <summary>Gets whether every output offset maps to the same input offset.</summary>
    public bool IsIdentity => _sourceStartOffsets is null;

    /// <summary>Creates an identity map without allocating per-character correction data.</summary>
    /// <param name="length">The input and output length in UTF-16 code units.</param>
    public static OffsetCorrectionMap Identity(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return new OffsetCorrectionMap(length, length, null, null);
    }

    /// <summary>
    /// Creates a map with one source range for each UTF-16 code unit in the transformed text.
    /// </summary>
    /// <param name="inputLength">The source text length in UTF-16 code units.</param>
    /// <param name="sourceStartOffsets">
    /// The source start offset for each transformed code unit. The array length is the transformed text length.
    /// </param>
    /// <param name="sourceEndOffsets">
    /// The exclusive source end offset for each transformed code unit. The array length is the transformed text length.
    /// </param>
    /// <returns>An identity map when the supplied ranges describe an unchanged coordinate system.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The source length or an offset is outside its valid range.</exception>
    /// <exception cref="ArgumentException">The start/end arrays differ in length or the ranges are not monotonic.</exception>
    public static OffsetCorrectionMap Create(
        int inputLength,
        ReadOnlySpan<int> sourceStartOffsets,
        ReadOnlySpan<int> sourceEndOffsets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputLength);
        if (sourceStartOffsets.Length != sourceEndOffsets.Length)
            throw new ArgumentException("Source start and end offset maps must have equal lengths.", nameof(sourceEndOffsets));

        bool identity = sourceStartOffsets.Length == inputLength;
        int previousStart = 0;
        int previousEnd = 0;
        for (int i = 0; i < sourceStartOffsets.Length; i++)
        {
            int start = sourceStartOffsets[i];
            int end = sourceEndOffsets[i];
            if (start < 0 || start > inputLength)
                throw new ArgumentOutOfRangeException(nameof(sourceStartOffsets), "A source start offset is outside the input.");
            if (end < start || end > inputLength)
                throw new ArgumentOutOfRangeException(nameof(sourceEndOffsets), "A source end offset is outside the input or before its start.");
            if (i > 0 && (start < previousStart || end < previousEnd))
                throw new ArgumentException("Source offset ranges must be monotonic.", nameof(sourceStartOffsets));

            identity &= start == i && end == i + 1;
            previousStart = start;
            previousEnd = end;
        }

        if (identity)
            return Identity(inputLength);

        return new OffsetCorrectionMap(
            inputLength,
            sourceStartOffsets.Length,
            sourceStartOffsets.ToArray(),
            sourceEndOffsets.ToArray());
    }

    /// <summary>Corrects a transformed token start offset to the input coordinate system.</summary>
    /// <param name="outputOffset">The transformed start offset, from zero through <see cref="OutputLength"/>.</param>
    /// <returns>The corresponding UTF-16 start offset in the input.</returns>
    public int CorrectStartOffset(int outputOffset)
    {
        ValidateOutputOffset(outputOffset);
        if (IsIdentity)
            return outputOffset;
        return outputOffset == OutputLength ? InputLength : _sourceStartOffsets![outputOffset];
    }

    /// <summary>Corrects a transformed token end offset to the input coordinate system.</summary>
    /// <param name="outputOffset">The transformed exclusive end offset, from zero through <see cref="OutputLength"/>.</param>
    /// <returns>The corresponding exclusive UTF-16 end offset in the input.</returns>
    public int CorrectEndOffset(int outputOffset)
    {
        ValidateOutputOffset(outputOffset);
        if (IsIdentity)
            return outputOffset;
        return outputOffset == 0 ? 0 : _sourceEndOffsets![outputOffset - 1];
    }

    /// <summary>
    /// Composes this map, from output to intermediate input, with a preceding map from that input to its source.
    /// </summary>
    /// <param name="previous">The map for the filter that ran immediately before this one.</param>
    /// <returns>A map from this output directly back to the original source.</returns>
    public OffsetCorrectionMap Compose(OffsetCorrectionMap previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (InputLength != previous.OutputLength)
            throw new ArgumentException("The preceding map output length must match this map input length.", nameof(previous));
        if (IsIdentity)
            return previous;
        if (previous.IsIdentity)
            return this;

        var starts = new int[OutputLength];
        var ends = new int[OutputLength];
        for (int i = 0; i < OutputLength; i++)
        {
            int intermediateStart = _sourceStartOffsets![i];
            int intermediateEnd = _sourceEndOffsets![i];
            int sourceStart = previous.CorrectStartOffset(intermediateStart);
            int sourceEnd = intermediateStart == intermediateEnd
                ? sourceStart
                : previous.CorrectEndOffset(intermediateEnd);
            starts[i] = sourceStart;
            ends[i] = sourceEnd;
        }

        return CreateOwned(previous.InputLength, starts, ends);
    }

    internal static OffsetCorrectionMap CreateOwned(int inputLength, int[] sourceStartOffsets, int[] sourceEndOffsets)
    {
        bool identity = sourceStartOffsets.Length == inputLength;
        for (int i = 0; identity && i < sourceStartOffsets.Length; i++)
            identity = sourceStartOffsets[i] == i && sourceEndOffsets[i] == i + 1;
        if (identity)
            return Identity(inputLength);
        return new OffsetCorrectionMap(inputLength, sourceStartOffsets.Length, sourceStartOffsets, sourceEndOffsets);
    }

    private void ValidateOutputOffset(int outputOffset)
    {
        if ((uint)outputOffset > (uint)OutputLength)
            throw new ArgumentOutOfRangeException(nameof(outputOffset));
    }
}
