using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Index;

[Trait("Category", "Chaos")]
[Trait("Category", "Index")]
public sealed class FieldPayloadChaosTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public FieldPayloadChaosTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Norms: Truncated Boost Tail Is Rejected")]
    public void Norms_TruncatedBoostTail_IsRejected()
    {
        var path = Path.Combine(_fixture.Path, $"norms-chaos-{Guid.NewGuid():N}.nrm");
        NormsWriter.Write(
            path,
            new Dictionary<string, float[]>(StringComparer.Ordinal) { ["body"] = [0.5f, 0.25f] },
            new Dictionary<string, float[]>(StringComparer.Ordinal) { ["body"] = [2.0f, 1.0f] });

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            stream.SetLength(stream.Length - 2);

        Assert.ThrowsAny<Exception>(() => NormsReader.Read(path));
    }

    [Fact(DisplayName = "Term Vectors: Truncated Payload Tail Is Rejected")]
    public void TermVectors_TruncatedPayloadTail_IsRejected()
    {
        var basePath = Path.Combine(_fixture.Path, $"tv-chaos-{Guid.NewGuid():N}");
        TermVectorsWriter.Write(
            basePath + ".tvd",
            basePath + ".tvx",
            [
                new Dictionary<string, List<TermVectorEntry>>(StringComparer.Ordinal)
                {
                    ["body"] = [new TermVectorEntry("hello", 1, [0], [new byte[] { 0xAA, 0xBB }])]
                }
            ]);

        // v3 trailer has 8-byte trailer at end; truncate past it into the body.
        using (var stream = new FileStream(basePath + ".tvd", FileMode.Open, FileAccess.Write, FileShare.None))
            stream.SetLength(stream.Length - 10);
        using var reader = TermVectorsReader.Open(basePath + ".tvd", basePath + ".tvx");
        Assert.ThrowsAny<Exception>(() => reader.GetTermVector(0));
    }
}
