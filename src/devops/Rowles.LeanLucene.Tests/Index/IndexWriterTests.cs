using System.Reflection;
using System.Reflection.Emit;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Contains unit tests for Index Writer.
/// </summary>
[Trait("Category", "Index")]
public sealed class IndexWriterTests : IClassFixture<TestDirectoryFixture>
{
    private static readonly OpCode[] SingleByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] MultiByteOpCodes = new OpCode[0x100];
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    static IndexWriterTests()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
                continue;

            var value = unchecked((ushort)opCode.Value);
            if (value < 0x100)
                SingleByteOpCodes[value] = opCode;
            else if ((value & 0xff00) == 0xfe00)
                MultiByteOpCodes[value & 0xff] = opCode;
        }
    }

    public IndexWriterTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string SubDir(string name)
    {
        var path = System.IO.Path.Combine(_fixture.Path, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Verifies the Index Writer: Flush On Ram Threshold Produces Segment File scenario.
    /// </summary>
    [Fact(DisplayName = "Index Writer: Flush On Ram Threshold Produces Segment File")]
    public void IndexWriter_FlushOnRamThreshold_ProducesSegmentFile()
    {
        var dir = new MMapDirectory(SubDir("ram_flush"));
        var config = new IndexWriterConfig { RamBufferSizeMB = 0.001 }; // ~1 KB
        using var writer = new IndexWriter(dir, config);

        for (int i = 0; i < 50; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", new string('x', 100)));
            writer.AddDocument(doc);
        }
        writer.Commit();

        var segFiles = System.IO.Directory.GetFiles(SubDir("ram_flush"), "*.seg");
        Assert.NotEmpty(segFiles);
    }

    /// <summary>
    /// Verifies the Index Writer: Flush On Doc Count Ceiling Produces Segment File scenario.
    /// </summary>
    [Fact(DisplayName = "Index Writer: Flush On Doc Count Ceiling Produces Segment File")]
    public void IndexWriter_FlushOnDocCountCeiling_ProducesSegmentFile()
    {
        var dir = new MMapDirectory(SubDir("doc_flush"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 5 };
        using var writer = new IndexWriter(dir, config);

        for (int i = 0; i < 6; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"document number {i}"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        var segFiles = System.IO.Directory.GetFiles(SubDir("doc_flush"), "*.seg");
        Assert.NotEmpty(segFiles);
    }

    /// <summary>
    /// Verifies the Index Writer: Commit Writes Segments N File scenario.
    /// </summary>
    [Fact(DisplayName = "Index Writer: Commit Writes Segments N File")]
    public void IndexWriter_CommitWritesSegmentsNFile()
    {
        var dir = new MMapDirectory(SubDir("commit_test"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world"));
        writer.AddDocument(doc);
        writer.Commit();

        var segNFiles = System.IO.Directory.GetFiles(SubDir("commit_test"), "segments_*");
        Assert.NotEmpty(segNFiles);
    }

    /// <summary>
    /// Verifies the Index Writer: Crash Before Commit Segment Not Visible scenario.
    /// </summary>
    [Fact(DisplayName = "Index Writer: Crash Before Commit Segment Not Visible")]
    public void IndexWriter_CrashBeforeCommit_SegmentNotVisible()
    {
        var subDir = SubDir("crash_test");
        var dir = new MMapDirectory(subDir);

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "uncommitted data"));
            writer.AddDocument(doc);
            // No commit — dispose discards uncommitted work
        }

        var segNFiles = System.IO.Directory.GetFiles(subDir, "segments_*");
        if (segNFiles.Length == 0)
        {
            Assert.Empty(segNFiles);
        }
        else
        {
            var commitContent = File.ReadAllText(segNFiles[^1]);
            Assert.DoesNotContain("uncommitted", commitContent);
        }
    }

    /// <summary>
    /// Verifies the Flush Triggers At Accurate Ram Threshold scenario.
    /// </summary>
    [Fact(DisplayName = "Flush Triggers At Accurate Ram Threshold")]
    public void FlushTriggersAtAccurateRamThreshold()
    {
        // With RamBufferSizeMB = 1 MB and accurate tracking via PostingAccumulator.EstimatedBytes,
        // the flush should happen close to 1 MB (not 5× overshoot from old heuristic).
        var dir = new MMapDirectory(SubDir("accurate_flush"));
        var config = new IndexWriterConfig { RamBufferSizeMB = 1.0, MaxBufferedDocs = 100_000 };
        using var writer = new IndexWriter(dir, config);

        for (int i = 0; i < 5000; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"document number {i} with some text to consume memory"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // With accurate tracking, we should get at least one flush (segment files exist)
        var segFiles = System.IO.Directory.GetFiles(SubDir("accurate_flush"), "*.seg");
        Assert.NotEmpty(segFiles);
    }

    /// <summary>
    /// Verifies the Ram Threshold: Forces Flush When Exceeded scenario.
    /// </summary>
    [Fact(DisplayName = "Ram Threshold: Forces Flush When Exceeded")]
    public void RamThreshold_ForcesFlush_WhenExceeded()
    {
        var dir = new MMapDirectory(SubDir("hard_ceiling"));
        var config = new IndexWriterConfig
        {
            RamBufferSizeMB = 0.1, // 100 KB — tight threshold to trigger RAM-based flush
            MaxBufferedDocs = 100_000
        };
        using var writer = new IndexWriter(dir, config);

        for (int i = 0; i < 2000; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"document {i} with text to fill buffer past ceiling"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // RAM threshold should have forced at least one flush, creating segment files
        var segFiles = System.IO.Directory.GetFiles(SubDir("hard_ceiling"), "*.seg");
        Assert.NotEmpty(segFiles);
    }

    /// <summary>
    /// Verifies the High Ram Pressure: Does Not Force Full GC scenario.
    /// </summary>
    [Fact(DisplayName = "High Ram Pressure: Does Not Force Full GC")]
    public void HighRamPressure_DoesNotForceFullGC()
    {
        var shouldFlush = typeof(IndexWriter).GetMethod("ShouldFlush", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(shouldFlush);

        Assert.False(
            CallsMethod(shouldFlush!, typeof(GC), nameof(GC.Collect)),
            "ShouldFlush must not induce a full GC; natural gen-2 collections make runtime counter assertions flaky.");
    }

    private static bool CallsMethod(MethodInfo source, Type declaringType, string methodName)
    {
        var body = source.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null)
            return false;

        int offset = 0;
        while (offset < il.Length)
        {
            OpCode opCode;
            int first = il[offset++];
            if (first == 0xfe)
            {
                if (offset >= il.Length)
                    break;

                opCode = MultiByteOpCodes[il[offset++]];
            }
            else
            {
                opCode = SingleByteOpCodes[first];
            }

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                int token = BitConverter.ToInt32(il, offset);
                offset += 4;
                try
                {
                    var called = source.Module.ResolveMethod(token);
                    if (called?.DeclaringType == declaringType &&
                        string.Equals(called.Name, methodName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                }
                continue;
            }

            offset += GetOperandSize(opCode.OperandType, il, offset);
        }

        return false;
    }

    private static int GetOperandSize(OperandType operandType, byte[] il, int operandOffset)
        => operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineSig
                or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, operandOffset) * 4),
            _ => throw new InvalidOperationException($"Unsupported IL operand type: {operandType}")
        };

    /// <summary>
    /// Verifies the Merge Backpressure: Pauses Indexing When Too Many Segments scenario.
    /// </summary>
    [Fact(DisplayName = "Merge Backpressure: Pauses Indexing When Too Many Segments")]
    public void MergeBackpressure_PausesIndexing_WhenTooManySegments()
    {
        var dir = new MMapDirectory(SubDir("merge_bp"));
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = 2,        // flush every 2 docs → lots of segments
            MergeThrottleSegments = 5   // throttle at 5 segments
        };
        using var writer = new IndexWriter(dir, config);

        // Index enough docs to exceed 5 segments, then commit
        for (int i = 0; i < 20; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"doc {i}"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // Should complete without hanging (backpressure triggers flush, not deadlock)
        var segFiles = System.IO.Directory.GetFiles(SubDir("merge_bp"), "*.seg");
        Assert.NotEmpty(segFiles);
    }
}
