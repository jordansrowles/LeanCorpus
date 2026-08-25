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
}
