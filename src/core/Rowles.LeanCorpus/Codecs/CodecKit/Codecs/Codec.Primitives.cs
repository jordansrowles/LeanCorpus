using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

public static partial class Codec
{
    // Fixed-size unsigned integers
    public static ICodec<byte> UInt8 { get; } = new Primitives.UInt8Codec();
    public static ICodec<ushort> UInt16LE { get; } = new Primitives.UInt16LECodec();
    public static ICodec<ushort> UInt16BE { get; } = new Primitives.UInt16BECodec();
    public static ICodec<uint> UInt32LE { get; } = new Primitives.UInt32LECodec();
    public static ICodec<uint> UInt32BE { get; } = new Primitives.UInt32BECodec();
    public static ICodec<ulong> UInt64LE { get; } = new Primitives.UInt64LECodec();
    public static ICodec<ulong> UInt64BE { get; } = new Primitives.UInt64BECodec();

    // Fixed-size signed integers
    public static ICodec<sbyte> Int8 { get; } = new Primitives.Int8Codec();
    public static ICodec<short> Int16LE { get; } = new Primitives.Int16LECodec();
    public static ICodec<short> Int16BE { get; } = new Primitives.Int16BECodec();
    public static ICodec<int> Int32LE { get; } = new Primitives.Int32LECodec();
    public static ICodec<int> Int32BE { get; } = new Primitives.Int32BECodec();
    public static ICodec<long> Int64LE { get; } = new Primitives.Int64LECodec();
    public static ICodec<long> Int64BE { get; } = new Primitives.Int64BECodec();

    // IEEE 754 floating-point
    public static ICodec<float> Float32LE { get; } = new Primitives.Float32LECodec();
    public static ICodec<float> Float32BE { get; } = new Primitives.Float32BECodec();
    public static ICodec<double> Float64LE { get; } = new Primitives.Float64LECodec();
    public static ICodec<double> Float64BE { get; } = new Primitives.Float64BECodec();

    // Variable-length integers (LEB128)
    public static ICodec<uint> VarUInt32 { get; } = new Primitives.VarUInt32Codec();
    public static ICodec<ulong> VarUInt64 { get; } = new Primitives.VarUInt64Codec();
    public static ICodec<int> VarInt32 { get; } = new Primitives.VarInt32Codec();
    public static ICodec<long> VarInt64 { get; } = new Primitives.VarInt64Codec();
}
