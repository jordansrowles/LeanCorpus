using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class ChecksumAlgorithmTests
{
    [Fact(DisplayName = "Crc32Provider round-trip with known data")]
    public void Crc32_RoundTrip()
    {
        var provider = new Crc32Provider();
        byte[] data = [0x01, 0x02, 0x03, 0x04];

        byte[] checksum = provider.Compute(new ReadOnlySequence<byte>(data));

        Assert.Equal(4, checksum.Length);
        Assert.True(provider.Verify(new ReadOnlySequence<byte>(data), checksum));
    }

    [Fact(DisplayName = "Crc32Provider empty input produces correct hash")]
    public void Crc32_EmptyInput()
    {
        var provider = new Crc32Provider();
        byte[] data = [];

        byte[] checksum = provider.Compute(new ReadOnlySequence<byte>(data));

        Assert.Equal(4, checksum.Length);
        Assert.True(provider.Verify(new ReadOnlySequence<byte>(data), checksum));
    }

    [Fact(DisplayName = "Crc32Provider wrong checksum fails verification")]
    public void Crc32_WrongChecksum_FailsVerification()
    {
        var provider = new Crc32Provider();
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        byte[] wrongChecksum = [0xFF, 0xFF, 0xFF, 0xFF];

        Assert.False(provider.Verify(new ReadOnlySequence<byte>(data), wrongChecksum));
    }

    [Fact(DisplayName = "Crc32Provider wrong checksum length fails verification")]
    public void Crc32_WrongChecksumLength_FailsVerification()
    {
        var provider = new Crc32Provider();
        byte[] data = [0x01];

        Assert.False(provider.Verify(new ReadOnlySequence<byte>(data), [0x00]));
        Assert.False(provider.Verify(new ReadOnlySequence<byte>(data), [0x00, 0x00, 0x00, 0x00, 0x00]));
    }

    [Fact(DisplayName = "Crc32Provider consistent hash for same data")]
    public void Crc32_ConsistentHash()
    {
        var provider = new Crc32Provider();
        byte[] data = new byte[1024];
        Random.Shared.NextBytes(data);

        byte[] checksum1 = provider.Compute(new ReadOnlySequence<byte>(data));
        byte[] checksum2 = provider.Compute(new ReadOnlySequence<byte>(data));

        Assert.Equal(checksum1, checksum2);
    }

    [Fact(DisplayName = "Crc32Provider different data produces different checksums")]
    public void Crc32_DifferentData_DifferentChecksum()
    {
        var provider = new Crc32Provider();
        byte[] data1 = [0x01, 0x02, 0x03];
        byte[] data2 = [0x01, 0x02, 0x04];

        byte[] checksum1 = provider.Compute(new ReadOnlySequence<byte>(data1));
        byte[] checksum2 = provider.Compute(new ReadOnlySequence<byte>(data2));

        Assert.NotEqual(checksum1, checksum2);
    }

    [Fact(DisplayName = "XxHash32Provider round-trip")]
    public void XxHash32_RoundTrip()
    {
        var provider = new XxHash32Provider();
        byte[] data = [0x01, 0x02, 0x03, 0x04];

        byte[] checksum = provider.Compute(new ReadOnlySequence<byte>(data));

        Assert.Equal(4, checksum.Length);
        Assert.True(provider.Verify(new ReadOnlySequence<byte>(data), checksum));
    }

    [Fact(DisplayName = "XxHash32Provider empty input")]
    public void XxHash32_EmptyInput()
    {
        var provider = new XxHash32Provider();
        byte[] data = [];

        byte[] checksum = provider.Compute(new ReadOnlySequence<byte>(data));

        Assert.Equal(4, checksum.Length);
        Assert.True(provider.Verify(new ReadOnlySequence<byte>(data), checksum));
    }

    [Fact(DisplayName = "XxHash64Provider round-trip")]
    public void XxHash64_RoundTrip()
    {
        var provider = new XxHash64Provider();
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];

        byte[] checksum = provider.Compute(new ReadOnlySequence<byte>(data));

        Assert.Equal(8, checksum.Length);
        Assert.True(provider.Verify(new ReadOnlySequence<byte>(data), checksum));
    }

    [Fact(DisplayName = "XxHash64Provider empty input")]
    public void XxHash64_EmptyInput()
    {
        var provider = new XxHash64Provider();
        byte[] data = [];

        byte[] checksum = provider.Compute(new ReadOnlySequence<byte>(data));

        Assert.Equal(8, checksum.Length);
        Assert.True(provider.Verify(new ReadOnlySequence<byte>(data), checksum));
    }

    [Fact(DisplayName = "All providers have consistent hashes for 1MB input")]
    public void AllProviders_1MB_Input_ConsistentHash()
    {
        byte[] data = new byte[1024 * 1024];
        Random.Shared.NextBytes(data);
        var seq = new ReadOnlySequence<byte>(data);

        var providers = new IChecksumProvider[]
        {
            new Crc32Provider(),
            new XxHash32Provider(),
            new XxHash64Provider(),
        };

        foreach (var provider in providers)
        {
            byte[] c1 = provider.Compute(seq);
            byte[] c2 = provider.Compute(seq);
            Assert.Equal(c1, c2);
        }
    }
}
