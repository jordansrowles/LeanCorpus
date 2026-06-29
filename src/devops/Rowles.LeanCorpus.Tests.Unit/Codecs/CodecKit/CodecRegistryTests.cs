using Rowles.LeanCorpus.Codecs.CodecKit.Checksum;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class CodecRegistryTests
{
    [Fact(DisplayName = "Empty registry contains no checksum providers")]
    public void Empty_HasNoProviders()
    {
        var registry = CodecRegistry.Empty;

        Assert.Throws<CodecValidationException>(() => registry.GetChecksumProvider(ChecksumAlgorithms.Crc32));
    }

    [Fact(DisplayName = "Default registry contains Crc32, XxHash32, XxHash64")]
    public void Default_ContainsExpectedProviders()
    {
        var registry = CodecRegistry.Default;

        Assert.NotNull(registry.GetChecksumProvider(ChecksumAlgorithms.Crc32));
        Assert.NotNull(registry.GetChecksumProvider(ChecksumAlgorithms.XxHash32));
        Assert.NotNull(registry.GetChecksumProvider(ChecksumAlgorithms.XxHash64));
    }

    [Fact(DisplayName = "Default is always the same instance")]
    public void Default_IsSameInstance()
    {
        Assert.Same(CodecRegistry.Default, CodecRegistry.Default);
    }

    [Fact(DisplayName = "WithChecksum adds a provider")]
    public void WithChecksum_AddsProvider()
    {
        var registry = CodecRegistry.Empty;
        var provider = new Crc32Provider();

        var updated = registry.WithChecksum(ChecksumAlgorithms.Crc32, provider);

        Assert.Same(provider, updated.GetChecksumProvider(ChecksumAlgorithms.Crc32));
        // Original registry unchanged
        Assert.Throws<CodecValidationException>(() => registry.GetChecksumProvider(ChecksumAlgorithms.Crc32));
    }

    [Fact(DisplayName = "GetChecksumProvider for unknown algorithm throws")]
    public void GetChecksumProvider_UnknownAlgorithm_Throws()
    {
        var registry = CodecRegistry.Empty;
        var ex = Assert.Throws<CodecValidationException>(() =>
            registry.GetChecksumProvider(new ChecksumAlgorithmId("nonexistent")));

        Assert.Equal(CodecErrorCode.UnknownAlgorithm, ex.ErrorCode);
    }

    [Fact(DisplayName = "Overwriting a checksum provider replaces it")]
    public void WithChecksum_OverwritesExisting()
    {
        var registry = CodecRegistry.Empty;
        var provider1 = new Crc32Provider();
        var provider2 = new Crc32Provider();

        var updated = registry.WithChecksum(ChecksumAlgorithms.Crc32, provider1)
                              .WithChecksum(ChecksumAlgorithms.Crc32, provider2);

        Assert.Same(provider2, updated.GetChecksumProvider(ChecksumAlgorithms.Crc32));
    }
}
