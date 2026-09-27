using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

/// <summary>
/// Unit tests for <see cref="CompressionCodecRegistry"/> gap branches:
/// <see cref="CompressionCodecRegistry.TryGet"/> false path and
/// <see cref="CompressionCodecRegistry.Get"/> unregistered-policy throw.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class CompressionCodecRegistryTests
{
    [Fact(DisplayName = "CompressionCodecRegistry.TryGet: Unregistered Policy Byte Returns False")]
    public void TryGet_UnregisteredPolicyByte_ReturnsFalse()
    {
        bool found = CompressionCodecRegistry.TryGet(0xFF, out var codec);
        Assert.False(found);
        Assert.Null(codec);
    }

    [Fact(DisplayName = "CompressionCodecRegistry.Get: Unregistered Policy Throws InvalidOperationException")]
    public void Get_UnregisteredPolicy_ThrowsInvalidOperationException()
    {
        var unregistered = (FieldCompressionPolicy)255;
        Assert.Throws<InvalidOperationException>(() => CompressionCodecRegistry.Get(unregistered));
    }

    [Fact(DisplayName = "CodecCatalogBuilder: duplicate compression policy bytes are rejected")]
    public void AddCompressionCodec_DuplicatePolicyByte_ThrowsAndKeepsFirstCodec()
    {
        const byte policyByte = 0xFE;
        var first = new TestCompressionCodec(policyByte);
        var replacement = new TestCompressionCodec(policyByte);
        var builder = new CodecCatalogBuilder().AddCompressionCodec(first);

        Assert.Throws<InvalidOperationException>(() => builder.AddCompressionCodec(replacement));

        var catalog = builder.Build();
        Assert.Same(first, catalog.GetCompressionCodec(policyByte));
    }

    [Fact(DisplayName = "CodecCatalogBuilder: explicit compression replacement is captured by its snapshot")]
    public void ReplaceCompressionCodec_BeforeBuild_ReplacesOnlyTheNewCatalog()
    {
        const byte policyByte = 0xFD;
        var original = new TestCompressionCodec(policyByte);
        var replacement = new TestCompressionCodec(policyByte);
        var builder = new CodecCatalogBuilder().AddCompressionCodec(original);

        var catalogBeforeReplacement = builder.Build();
        builder.ReplaceCompressionCodec(replacement);
        var catalogAfterReplacement = builder.Build();

        Assert.Same(original, catalogBeforeReplacement.GetCompressionCodec(policyByte));
        Assert.Same(replacement, catalogAfterReplacement.GetCompressionCodec(policyByte));
    }

    private sealed class TestCompressionCodec(byte policyByte) : IFieldCompressionCodec
    {
        public byte PolicyByte => policyByte;

        public byte[] Compress(ReadOnlySpan<byte> raw) => raw.ToArray();

        public byte[] Decompress(ReadOnlySpan<byte> compressed, int originalSize)
        {
            if (compressed.Length != originalSize)
                throw new InvalidDataException("Test compression payload length mismatch.");
            return compressed.ToArray();
        }
    }
}
