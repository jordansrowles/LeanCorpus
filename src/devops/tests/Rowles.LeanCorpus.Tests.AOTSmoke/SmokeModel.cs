using Rowles.LeanCorpus.Mapping.Attributes;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

[LeanDocument]
public partial class SmokeModel
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanText("title")]
    public string? Title { get; init; }

    [LeanNumeric("count")]
    public int Count { get; init; }
}
