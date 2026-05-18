namespace Rowles.LeanCorpus.Codecs.DocValues;

internal sealed class NormsData
{
    internal Dictionary<string, byte[]> Norms { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, float[]> Boosts { get; } = new(StringComparer.Ordinal);
}
