using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures the streaming and checksum cost of canonical Frame v1 against the 2.x trailer.</summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class CodecFrameBenchmarks
{
    private string _directory = string.Empty;
    private byte[] _block = [];

    [Params(1, 16)]
    public int BodyMiB { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "LeanCorpus_CodecFrameBenchmarks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _block = new byte[64 * 1024];
        new Random(42).NextBytes(_block);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Benchmark(Baseline = true, Description = "Legacy trailer")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long LegacyTrailer()
    {
        string path = Path.Combine(_directory, "legacy.pos");
        using var output = new IndexOutput(path);
        var descriptor = CodecCatalog.Default.GetFile("leancorpus.postings.data");
        using var frame = CodecFileHeader.BeginStreamingWrite(
            output,
            checked((byte)descriptor.CurrentFormatVersion!.Value));
        WriteBody(output);
        return output.Position;
    }

    [Benchmark(Description = "Canonical xxHash64 frame")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long CanonicalFrame()
    {
        string path = Path.Combine(_directory, "canonical.pos");
        using var output = new IndexOutput(path);
        var descriptor = CodecCatalog.Default.GetFile("leancorpus.postings.data");
        using var frame = CodecFileWriter.Begin(output, descriptor);
        WriteBody(frame.Output);
        frame.Complete();
        return frame.Output.Position;
    }

    private void WriteBody(IndexOutput output)
    {
        int remaining = BodyMiB * 1024 * 1024;
        while (remaining > 0)
        {
            int count = Math.Min(remaining, _block.Length);
            output.WriteBytes(_block.AsSpan(0, count));
            remaining -= count;
        }
    }

    private void WriteBody(CodecBodyOutput output)
    {
        int remaining = BodyMiB * 1024 * 1024;
        while (remaining > 0)
        {
            int count = Math.Min(remaining, _block.Length);
            output.WriteBytes(_block.AsSpan(0, count));
            remaining -= count;
        }
    }
}
