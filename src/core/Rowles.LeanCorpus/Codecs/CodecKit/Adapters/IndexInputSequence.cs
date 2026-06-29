using System.Buffers;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Adapters;

/// <summary>
/// Adapts an <see cref="IndexInput"/> (mmap-backed sequential reader) to a
/// <see cref="ReadOnlySequence{T}"/> for use with CodecKit's <see cref="SequenceReader{T}"/>.
/// </summary>
/// <remarks>
/// The entire mapped region from the current <see cref="IndexInput.Position"/> to
/// <see cref="IndexInput.Length"/> is exposed as a single-segment sequence.
/// After decode, the caller MUST call <see cref="SyncPositionBack"/> to update the
/// IndexInput position to match how far the SequenceReader advanced.
/// </remarks>
internal readonly struct IndexInputSequence
{
    private readonly ReadOnlySequence<byte> _sequence;
    private readonly IndexInput _input;
    private readonly long _startPosition;

    public IndexInputSequence(IndexInput input)
    {
        _input = input;
        _startPosition = input.Position;
        long remaining = input.Length - _startPosition;
        int remainingInt = (int)Math.Min(remaining, int.MaxValue);

        // Read the remaining bytes into a contiguous buffer for SequenceReader
        // (small overhead — headers are small; body navigation uses IndexInput directly)
        var bytes = input.ReadSpan(remainingInt).ToArray();
        var memory = new ReadOnlyMemory<byte>(bytes);
        _sequence = new ReadOnlySequence<byte>(memory);
        // Reset position so SyncPositionBack works correctly
        input.Seek(_startPosition);
    }

    /// <summary>The single-segment sequence for CodecKit's SequenceReader.</summary>
    public ReadOnlySequence<byte> Sequence => _sequence;

    /// <summary>
    /// Returns the original IndexInput. Use for seeking within the mapped region
    /// for lazy decode patterns.
    /// </summary>
    public IndexInput Input => _input;

    /// <summary>Starting byte offset within the file.</summary>
    public long StartPosition => _startPosition;

    /// <summary>
    /// After a decode operation advances the SequenceReader, call this to
    /// synchronise the IndexInput's position.
    /// </summary>
    public void SyncPositionBack(ref SequenceReader<byte> reader)
    {
        long consumed = (long)reader.Consumed;
        _input.Seek(_startPosition + consumed);
    }
}
