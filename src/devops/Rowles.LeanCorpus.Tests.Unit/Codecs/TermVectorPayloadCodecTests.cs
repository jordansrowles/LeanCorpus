using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

[Trait("Category", "Codecs")]
public sealed class TermVectorPayloadCodecTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public TermVectorPayloadCodecTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Term Vectors: Payloads Round-Trip")]
    public void TermVectors_Payloads_RoundTrip()
    {
        var path = Path.Combine(_fixture.Path, $"tv-payload-{Guid.NewGuid():N}");
        var docs = new Dictionary<string, List<TermVectorEntry>>[]
        {
            new(StringComparer.Ordinal)
            {
                ["body"] =
                [
                    new TermVectorEntry("hello", 2, [1, 3], [new byte[] { 0x01 }, null]),
                    new TermVectorEntry("world", 1, [2], [new byte[] { 0x02, 0x03 }])
                ]
            }
        };

        TermVectorsWriter.Write(path + ".tvd", path + ".tvx", docs);

        using var reader = TermVectorsReader.Open(path + ".tvd", path + ".tvx");
        var termVectors = reader.GetTermVector(0);
        var hello = Assert.Single(termVectors["body"].Where(static entry => entry.Term == "hello"));
        var world = Assert.Single(termVectors["body"].Where(static entry => entry.Term == "world"));

        Assert.Equal(new byte[] { 0x01 }, hello.Payloads![0]);
        Assert.Null(hello.Payloads![1]);
        Assert.Equal(new byte[] { 0x02, 0x03 }, world.Payloads![0]);
    }
}
