using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Enums;

/// <summary>
/// Defines the padding strategy for fixed-size frames.
/// </summary>
public abstract class FramePadding
{
    private FramePadding() { }

    /// <summary>Payload must be exactly the frame size. No padding is added or expected.</summary>
    public static FramePadding Exact { get; } = new ExactPadding();

    /// <summary>Remaining bytes are filled with zeroes on encode and verified as zeroes on decode.</summary>
    public static FramePadding ZeroFill { get; } = new ByteFillPadding(0x00);

    /// <summary>Remaining bytes are filled with a chosen byte on encode and verified on decode.</summary>
    public static FramePadding ByteFill(byte value) => new ByteFillPadding(value);

    internal abstract byte? FillByte { get; }
    internal abstract bool IsExact { get; }

    private sealed class ExactPadding : FramePadding
    {
        internal override byte? FillByte => null;
        internal override bool IsExact => true;
        public override string ToString() => "Exact";
    }

    internal sealed class ByteFillPadding : FramePadding
    {
        internal ByteFillPadding(byte value) => FillByte = value;
        internal override byte? FillByte { get; }
        internal override bool IsExact => false;
        public override string ToString() => FillByte == 0x00 ? "ZeroFill" : $"ByteFill(0x{FillByte:X2})";
    }
}
