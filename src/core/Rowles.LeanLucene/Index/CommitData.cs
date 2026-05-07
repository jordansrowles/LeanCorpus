namespace Rowles.LeanLucene.Index;

internal sealed class CommitData
{
    public List<string> Segments { get; set; } = [];

    public int Generation { get; set; }

    public long ContentToken { get; set; }
}
