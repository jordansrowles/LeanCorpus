using System;
using System.Buffers;
using System.Collections.Generic;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Recovery;

/// <summary>
/// Provides static methods for recovering data from potentially corrupt byte streams.
/// </summary>
internal static class CodecRecovery
{
    /// <summary>
    /// Scans for all occurrences of a magic byte pattern in the source sequence.
    /// Overlapping matches are returned. Returns byte offsets where the pattern occurs.
    /// </summary>
    public static IReadOnlyList<long> ScanForMagic(
        ReadOnlySequence<byte> source,
        ReadOnlySpan<byte> magicBytes)
    {
        var results = new List<long>();

        if (magicBytes.IsEmpty)
            return results;

        long sourceLength = source.Length;
        if (magicBytes.Length > sourceLength)
            return results;

        ReadOnlySpan<byte> buffer = source.IsSingleSegment
            ? source.First.Span
            : source.ToArray();

        int patternLength = magicBytes.Length;
        int limit = buffer.Length - patternLength;

        for (int i = 0; i <= limit; i++)
        {
            if (buffer.Slice(i, patternLength).SequenceEqual(magicBytes))
                results.Add(i);
        }

        return results;
    }

    /// <summary>
    /// Iterates through a byte stream, attempting to decode each frame.
    /// On success, yields the decoded value and advances past the frame.
    /// On failure, yields failure metadata and advances by one byte.
    /// </summary>
    public static IReadOnlyList<FrameScanResult<T>> ScanFrames<T>(
        ReadOnlySequence<byte> source,
        ICodec<T> frameCodec,
        CodecOptions? options = null,
        CodecRegistry? registry = null)
    {
        var opts = options ?? CodecOptions.Default;
        var reg = registry ?? CodecRegistry.Default;
        var results = new List<FrameScanResult<T>>();

        long position = 0;

        while (position < source.Length)
        {
            long offset = position;

            try
            {
                var remaining = source.Slice(position);
                var reader = new SequenceReader<byte>(remaining);
                var context = new CodecContext(opts, reg);
                T value = frameCodec.Decode(ref reader, context);
                position += reader.Consumed;
                results.Add(new FrameScanResult<T>(offset, value));
            }
            catch (CodecException ex)
            {
                position++;

                var failure = new CodecFailure(
                    ex.ErrorCode,
                    ex.ByteOffset,
                    ex.Path,
                    ex.Message,
                    ex);

                results.Add(new FrameScanResult<T>(offset, failure));
            }
        }

        return results;
    }
}
