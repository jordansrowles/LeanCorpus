using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.CodecKit;

[Trait("Category", "Chaos")]
[Trait("Category", "CodecKit")]
public sealed class HeaderFuzzTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public HeaderFuzzTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 100)]
    public void RandomBytes_AsHeader_BinaryReader_DoesNotCrash(byte[] randomBytes)
    {
        var path = Path.Combine(_fixture.Path, $"hf_bw_{Guid.NewGuid():N}.dat");
        File.WriteAllBytes(path, randomBytes);

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new System.IO.BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        try
        {
            CodecFileHeader.Read(reader, CodecFormats.Postings);
            // Success is fine
        }
        catch (Exception)
        {
            // Exception is fine too — just must not be an uncatchable crash
        }
    }

    [Property(MaxTest = 100)]
    public void RandomBytes_AsHeader_IndexInput_DoesNotCrash(byte[] randomBytes)
    {
        var path = Path.Combine(_fixture.Path, $"hf_ii_{Guid.NewGuid():N}.dat");
        File.WriteAllBytes(path, randomBytes);

        using var input = new IndexInput(path);

        try
        {
            CodecFileHeader.Read(input, CodecFormats.Postings);
        }
        catch (Exception)
        {
            // Exception acceptable, crash is not
        }
    }

    [Property(MaxTest = 50)]
    public void RoundTrip_AnyBody_ReturnsSameVersionAndBody(byte[] body)
    {
        var path = Path.Combine(_fixture.Path, $"hf_rt_{Guid.NewGuid():N}.dat");

        using (var output = new IndexOutput(path))
        {
            CodecFileHeader.Write(output, CodecFormats.Postings, body);
            output.Flush();
        }

        using var input = new IndexInput(path);
        var result = CodecFileHeader.Read(input, CodecFormats.Postings);

        Assert.Equal(CodecFormats_decode_version(CodecFormats.Postings), result.Version);
        Assert.Equal(body, result.Body);
    }

    private static byte CodecFormats_decode_version(ICodec<byte[]> format)
    {
        // Write a 1-byte body and read version
        using var ms = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            CodecFileHeader.Write(bw, format, [0x00]);

        byte[] data = ms.ToArray();
        return data[0]; // first byte is the version
    }

    [Property(MaxTest = 50)]
    public void ReadVersion_FollowedByReadRemaining_MatchesOriginalBody(byte[] body)
    {
        var path = Path.Combine(_fixture.Path, $"hf_ver_{Guid.NewGuid():N}.dat");

        using (var output = new IndexOutput(path))
        {
            CodecFileHeader.Write(output, CodecFormats.Postings, body);
            output.Flush();
        }

        using var input = new IndexInput(path);
        byte version = CodecFileHeader.ReadVersion(input, CodecFormats.Postings);

        // Read remaining bytes (past header)
        long remaining = input.Length - input.Position;
        byte[] remainingBytes = new byte[remaining];
        for (long i = 0; i < remaining; i++)
            remainingBytes[i] = input.ReadByte();

        Assert.Equal(body, remainingBytes);
    }
}
