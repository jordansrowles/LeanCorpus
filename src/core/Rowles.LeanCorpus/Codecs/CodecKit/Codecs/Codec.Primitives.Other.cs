using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using System;
using System.Buffers;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

public static partial class Codec
{
    public static ICodec<bool> Bool { get; } = new Primitives.BoolCodec();
    public static ICodec<Guid> GuidRfc4122 { get; } = new Primitives.GuidRfc4122Codec();
    public static ICodec<Guid> GuidDotNet { get; } = new Primitives.GuidDotNetCodec();

    public static ICodec<Unit> Skip(int n) => new Primitives.SkipCodec(n);
    public static ICodec<Unit> Padding(int length, byte value = 0x00) => new Primitives.PaddingCodec(length, value);
    public static ICodec<Unit> Magic(params byte[] pattern) => new Primitives.MagicCodec(pattern);
    public static ICodec<Unit> Magic(uint value) => new Primitives.MagicCodec(value);

    public static ICodec<byte[]> BytesOwned(int length) => new Primitives.BytesOwnedCodec(length);
    public static ICodec<ReadOnlySequence<byte>> BytesBorrowed(int length) => new Primitives.BytesBorrowedCodec(length);
    public static ICodec<byte[]> BytesOwnedRemaining() => new Primitives.BytesOwnedRemainingCodec();
    public static ICodec<ReadOnlySequence<byte>> BytesBorrowedRemaining() => new Primitives.BytesBorrowedRemainingCodec();

    public static ICodec<string> Utf8String(int byteLength) => new Primitives.Utf8StringCodec(byteLength);
    public static ICodec<string> Utf8StringRemaining() => new Primitives.Utf8StringRemainingCodec();

    public static ICodec<byte[]> Utf8BytesOwned(int byteLength) => new Primitives.Utf8BytesOwnedCodec(byteLength);
    public static ICodec<ReadOnlySequence<byte>> Utf8BytesBorrowed(int byteLength) => new Primitives.Utf8BytesBorrowedCodec(byteLength);
}
