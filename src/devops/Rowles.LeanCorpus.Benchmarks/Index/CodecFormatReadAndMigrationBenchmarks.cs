using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures structural frame opening separately from streamed checksum validation.</summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class CodecFrameReadBenchmarks
{
    private string _directory = string.Empty;
    private string _path = string.Empty;
    private CodecFileDescriptor? _descriptor;

    [Params(1, 16)]
    public int BodyMiB { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "LeanCorpus_CodecFrameReadBenchmarks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "canonical.pos");
        _descriptor = CodecCatalog.Default.GetFile("leancorpus.postings.data");
        byte[] block = new byte[64 * 1024];
        new Random(42).NextBytes(block);
        using var output = new IndexOutput(_path);
        using var frame = CodecFileWriter.Begin(output, _descriptor);
        for (int remaining = BodyMiB * 1024 * 1024; remaining > 0;)
        {
            int count = Math.Min(remaining, block.Length);
            frame.Output.WriteBytes(block.AsSpan(0, count));
            remaining -= count;
        }
        frame.Complete();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long OpenFrame()
    {
        using var input = new IndexInput(_path);
        using var frame = CodecFileReader.Open(input, _descriptor!);
        return frame.Metadata.BodyLength;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long ValidateChecksum()
    {
        using var input = new IndexInput(_path);
        using var frame = CodecFileReader.Open(input, _descriptor!);
        frame.ValidateChecksum();
        return frame.Metadata.BodyLength;
    }
}

/// <summary>Measures migration throughput and allocation for a large streamed reframe action.</summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[InvocationCount(1)]
public class CodecMigrationBenchmarks
{
    private string _sourcePath = string.Empty;
    private string _iterationPath = string.Empty;

    [Params(16, 64)]
    public int BodyMiB { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _sourcePath = Path.Combine(Path.GetTempPath(), "LeanCorpus_CodecMigrationBenchmarks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sourcePath);
        using (var writer = new IndexWriter(new MMapDirectory(_sourcePath), new IndexWriterConfig()))
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "codec migration benchmark"));
            document.Add(new NumericField("value", 42));
            writer.AddDocument(document);
            writer.Commit();
        }

        RewriteNumericDocValuesAsLargeLegacyTrailer();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _iterationPath = _sourcePath + "-iteration";
        if (Directory.Exists(_iterationPath))
            Directory.Delete(_iterationPath, recursive: true);
        CopyDirectory(_sourcePath, _iterationPath);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        if (Directory.Exists(_iterationPath))
            Directory.Delete(_iterationPath, recursive: true);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_sourcePath))
            Directory.Delete(_sourcePath, recursive: true);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool ReframeMigration()
    {
        using var directory = new MMapDirectory(_iterationPath);
        return IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
        {
            DryRun = false,
            ValidateBeforeMigration = false,
            ValidateAfterMigration = false,
        }).Succeeded;
    }

    private void RewriteNumericDocValuesAsLargeLegacyTrailer()
    {
        string path = Directory.GetFiles(_sourcePath, "*.dvn").Single();
        var descriptor = CodecCatalog.Default.GetFile("leancorpus.doc-values.numeric");
        byte[] body;
        using (var input = new IndexInput(path))
        using (var frame = CodecFileReader.Open(input, descriptor))
            body = frame.ReadBody();

        using var output = new IndexOutput(path);
        using var legacyFrame = CodecFileHeader.BeginStreamingWrite(
            output,
            checked((byte)descriptor.CurrentFormatVersion!.Value));
        output.WriteBytes(body);
        byte[] padding = new byte[64 * 1024];
        for (int remaining = BodyMiB * 1024 * 1024 - body.Length; remaining > 0;)
        {
            int count = Math.Min(remaining, padding.Length);
            output.WriteBytes(padding.AsSpan(0, count));
            remaining -= count;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    }
}
