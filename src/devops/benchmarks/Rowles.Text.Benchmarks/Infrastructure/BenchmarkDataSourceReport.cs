namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Describes a real-world data source loaded into the benchmark data pool.</summary>
internal sealed class BenchmarkDataSourceReport
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long ByteCount { get; set; }
    public int DocumentCount { get; set; }
    public string FingerprintSha256 { get; set; } = string.Empty;
    public bool FallbackUsed { get; set; }
}
