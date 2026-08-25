using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>
/// Immutable configuration for codec operations. All limits are validated at construction time.
/// </summary>
public sealed record CodecOptions
{
    private readonly long _maxFrameBytes = 64 * 1024 * 1024;
    private readonly long _maxMaterialisedBodyBytes = 64 * 1024 * 1024;
    private readonly long _maxCodecFileBytes = long.MaxValue;
    private readonly int _maxSequenceElements = 1_000_000;
    private readonly int _maxStringBytes = 16 * 1024 * 1024;
    private readonly int _maxScratchBufferBytes = 64 * 1024 * 1024;
    private readonly long _maxDecompressedBytes = 256 * 1024 * 1024;
    private readonly int _maxNestingDepth = 64;

    public long MaxFrameBytes
    {
        get => _maxFrameBytes;
        init
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(MaxFrameBytes), value, "Must be positive.");
            _maxFrameBytes = value;
        }
    }

    /// <summary>
    /// Gets the largest codec-file body that an operation may materialise into one managed buffer.
    /// This limit does not restrict streaming or random-access file sizes.
    /// </summary>
    public long MaxMaterialisedBodyBytes
    {
        get => _maxMaterialisedBodyBytes;
        init
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(MaxMaterialisedBodyBytes), value, "Must be positive.");
            _maxMaterialisedBodyBytes = value;
        }
    }

    /// <summary>
    /// Gets the largest physical codec file accepted by file-level framing operations.
    /// </summary>
    public long MaxCodecFileBytes
    {
        get => _maxCodecFileBytes;
        init
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(MaxCodecFileBytes), value, "Must be positive.");
            _maxCodecFileBytes = value;
        }
    }

    public int MaxSequenceElements
    {
        get => _maxSequenceElements;
        init
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(MaxSequenceElements), value, "Must be positive.");
            _maxSequenceElements = value;
        }
    }

    public int MaxStringBytes
    {
        get => _maxStringBytes;
        init
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(MaxStringBytes), value, "Must be positive.");
            _maxStringBytes = value;
        }
    }

    public int MaxScratchBufferBytes
    {
        get => _maxScratchBufferBytes;
        init
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(MaxScratchBufferBytes), value, "Must be positive.");
            _maxScratchBufferBytes = value;
        }
    }

    public long MaxDecompressedBytes
    {
        get => _maxDecompressedBytes;
        init
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(MaxDecompressedBytes), value, "Must be positive.");
            _maxDecompressedBytes = value;
        }
    }

    public int MaxNestingDepth
    {
        get => _maxNestingDepth;
        init
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(MaxNestingDepth), value, "Must be positive.");
            _maxNestingDepth = value;
        }
    }

    public bool RejectNonCanonicalVarInts { get; init; } = true;
    public Utf8ValidationMode Utf8Validation { get; init; } = Utf8ValidationMode.Strict;

    public static CodecOptions Default { get; } = new();
}
