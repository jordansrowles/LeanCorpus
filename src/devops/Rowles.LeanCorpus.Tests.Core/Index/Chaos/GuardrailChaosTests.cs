using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Tests.Core.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Core.Compatibility;
[Category(TestCategory.Chaos)]
[Area(TestArea.Index)]
public sealed class GuardrailChaosTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public GuardrailChaosTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 8)]
    public void FutureTermDictionaryVersions_AreAlwaysRejected(PositiveInt delta)
    {
        using var directory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "future_guardrail");
        var version = CodecConstants.TermDictionaryVersion + 1 + delta.Get % Math.Max(1, byte.MaxValue - CodecConstants.TermDictionaryVersion - 1);
        WriteCodecVersion(Directory.GetFiles(directory.DirectoryPath, "*.dic").Single(), version);

        var result = IndexCompatibility.Check(directory);

        Assert.Equal(IndexCompatibilityStatus.UnsupportedFutureFormat, result.Status);
        Assert.False(result.CanRead);
        Assert.False(result.CanWrite);
        Assert.False(result.CanValidate);
        Assert.True(result.MustReject);
        Assert.Throws<InvalidDataException>(() => new IndexSearcher(directory));
    }

    private static void WriteCodecVersion(string path, int version)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = 6;
        stream.Write(BitConverter.GetBytes(version));
    }
}
