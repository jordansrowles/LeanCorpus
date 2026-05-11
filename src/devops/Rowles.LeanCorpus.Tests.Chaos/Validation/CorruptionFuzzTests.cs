using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Validation;

[Trait("Category", "Chaos")]
[Trait("Category", "Validation")]
public sealed class CorruptionFuzzTests : IClassFixture<ChaosDirectoryFixture>
{
    private static readonly HashSet<string> MutatableExtensions = [".dic", ".pos", ".fdt", ".fdx", ".seg"];
    private readonly ChaosDirectoryFixture _fixture;

    public CorruptionFuzzTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 16)]
    public void Validator_HeaderByteMutation_ReturnsStructuredRepairAdvice(NonNegativeInt selector)
    {
        using var directory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "validator_header_flip");
        var files = Directory
            .GetFiles(directory.DirectoryPath)
            .Where(static path => MutatableExtensions.Contains(System.IO.Path.GetExtension(path)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(files);

        var target = files[selector.Get % files.Length];
        FlipByte(target, offset: 0);

        var result = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true });

        Assert.False(result.IsHealthy);
        Assert.NotEmpty(result.DetailedIssues);
        Assert.All(result.DetailedIssues, static issue => Assert.NotEmpty(issue.SuggestedActions));
    }

    private static void FlipByte(string path, long offset)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = offset;
        var value = stream.ReadByte();
        Assert.NotEqual(-1, value);
        stream.Position = offset;
        stream.WriteByte((byte)(value ^ 0x5A));
    }
}
