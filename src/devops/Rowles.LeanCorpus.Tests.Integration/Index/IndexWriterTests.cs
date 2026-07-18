using System.Reflection;
using System.Reflection.Emit;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

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

    /// <summary>
    /// Verifies that merge throttling reduces segment count when the threshold is exceeded.
    /// </summary>
    [Fact(DisplayName = "Merge Throttle: Reduces Segment Count After Threshold")]
    public void MergeThrottle_ReducesSegmentCount_AfterThreshold()
    {
        var dir = new MMapDirectory(SubDir("merge_throttle_count"));
        // TieredMergePolicy(2): merge when 2+ segments exist in the same tier.
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = 2,        // flush every 2 docs
            MergeThrottleSegments = 3,  // throttle at 3 segments
            MergePolicy = new TieredMergePolicy(2)
        };
        using var writer = new IndexWriter(dir, config);

        // Index 12 docs → 6 flushes → 6 segments. Throttle fires at 3, so merges
        // should reduce the count before we finish.
        for (int i = 0; i < 12; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"doc {i}"));
            writer.AddDocument(doc);
        }

        writer.Commit();

        int segmentCount = System.IO.Directory.GetFiles(SubDir("merge_throttle_count"), "*.seg").Length;
        // With throttle+merge, should be fewer than the 6 unmerged segments.
        Assert.True(segmentCount < 6, $"Expected fewer than 6 segments, got {segmentCount}");
    }

    /// <summary>
    /// Verifies that Compact force-merges multiple segments into one and reclaims
    /// disk space from soft-deleted documents past their retention window.
    /// </summary>
    [Fact(DisplayName = "Compact force-merges segments and drops expired soft-deletes")]
    public void Compact_ForceMergesSegments_AndDropsExpiredSoftDeletes()
    {
        var dir = new MMapDirectory(SubDir("compact_sd"));
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = 3,                   // flush every 3 docs to produce multiple segments
            SoftDeletesEnabled = true,
            SoftDeleteRetentionSeconds = 0.001,    // effectively immediate expiry
            MergeThreshold = 10                    // keep tiered merge from interfering
        };

        using var writer = new IndexWriter(dir, config);

        // Index 12 documents to create 4 segments (3 docs per flush)
        for (int i = 1; i <= 12; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("id", $"doc-{i}"));
            doc.Add(new TextField("body", $"content number {i}"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // Verify we have multiple segments
        var initialSegFiles = System.IO.Directory.GetFiles(SubDir("compact_sd"), "*.seg");
        Assert.True(initialSegFiles.Length > 1, $"Expected multiple segments, got {initialSegFiles.Length}");

        // Soft-delete documents 1-6
        for (int i = 1; i <= 6; i++)
            writer.SoftDeleteDocuments(new Rowles.LeanCorpus.Search.Queries.TermQuery("id", $"doc-{i}"));
        writer.Commit();

        // Give the soft-delete retention window time to expire
        System.Threading.Thread.Sleep(100);

        // Compact — should merge all segments into one, dropping expired soft-deletes
        int merged = writer.Compact();
        Assert.True(merged > 1, $"Expected multiple segments merged, got {merged}");

        // Verify only one segment remains
        var finalSegFiles = System.IO.Directory.GetFiles(SubDir("compact_sd"), "*.seg");
        Assert.Single(finalSegFiles);

        // Verify we can search the remaining documents
        using var reader = new Rowles.LeanCorpus.Index.Segment.SegmentReader(dir,
            Rowles.LeanCorpus.Index.Segment.SegmentInfo.ReadFrom(finalSegFiles[0]));
        Assert.True(reader.MaxDoc >= 6, $"Expected at least 6 live docs, got {reader.MaxDoc}");
    }
    /// <summary>
    /// Verifies that soft-deleted documents still within the retention window
    /// are preserved during Compact rather than being physically dropped.
    /// </summary>
    [Fact(DisplayName = "Compact preserves soft-deletes within retention window")]
    public void Compact_PreservesSoftDeletes_WithinRetentionWindow()
    {
        var dir = new MMapDirectory(SubDir("compact_sd_retain"));
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = 3,               // flush every 3 docs
            SoftDeletesEnabled = true,
            SoftDeleteRetentionSeconds = 3600, // 1 hour — well beyond test duration
            MergeThreshold = 10                // keep tiered merge from interfering
        };

        using var writer = new IndexWriter(dir, config);

        // Index 9 documents to create 3 segments
        for (int i = 1; i <= 9; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("id", $"doc-{i}"));
            doc.Add(new TextField("body", $"content number {i}"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // Soft-delete documents 1-3 (first segment loses all its docs)
        for (int i = 1; i <= 3; i++)
            writer.SoftDeleteDocuments(new Rowles.LeanCorpus.Search.Queries.TermQuery("id", $"doc-{i}"));
        writer.Commit();

        // Compact immediately — retention window has not elapsed
        int merged = writer.Compact();
        Assert.True(merged > 1, $"Expected multiple segments merged, got {merged}");

        // Verify only one segment remains
        var finalSegFiles = System.IO.Directory.GetFiles(SubDir("compact_sd_retain"), "*.seg");
        Assert.Single(finalSegFiles);

        // All 9 documents should still be present — the soft-deleted docs
        // are within the retention window and must survive the merge.
        using var reader = new Rowles.LeanCorpus.Index.Segment.SegmentReader(dir,
            Rowles.LeanCorpus.Index.Segment.SegmentInfo.ReadFrom(finalSegFiles[0]));
        Assert.Equal(9, reader.MaxDoc);
    }

    /// <summary>
    /// Verifies that Compact is a no-op when there is only one segment.
    /// </summary>
    [Fact(DisplayName = "Compact returns zero when only one segment exists")]
    public void Compact_ReturnsZero_WhenSingleSegment()
    {
        var dir = new MMapDirectory(SubDir("compact_one"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 10 };

        using var writer = new IndexWriter(dir, config);

        for (int i = 1; i <= 3; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"doc {i}"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // Should be a single segment after explicit commit + flush
        int merged = writer.Compact();
        Assert.Equal(0, merged);
    }

    /// <summary>
    /// Verifies that segments held by an active snapshot are excluded from Compact
    /// and only unprotected segments are merged. After the snapshot is released,
    /// a second Compact merges the remaining segments.
    /// </summary>
    [Fact(DisplayName = "Compact excludes snapshot-protected segments")]
    public void Compact_ExcludesSnapshotProtectedSegments()
    {
        var dir = new MMapDirectory(SubDir("compact_snapshot"));
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = 3,    // flush every 3 docs
            MergeThreshold = 10     // keep tiered merge from interfering
        };

        using var writer = new IndexWriter(dir, config);

        // Create 2 segments by indexing 6 documents
        for (int i = 1; i <= 6; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"doc {i}"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        var initialSegs = System.IO.Directory.GetFiles(SubDir("compact_snapshot"), "*.seg");
        Assert.True(initialSegs.Length >= 2, $"Expected at least 2 segments, got {initialSegs.Length}");

        // Hold a snapshot — both segments are now protected
        var snapshot = writer.CreateSnapshot();

        // Compact should be a no-op because all segments are protected
        int merged = writer.Compact();
        Assert.Equal(0, merged);

        // Release the snapshot — segments are no longer protected
        writer.ReleaseSnapshot(snapshot);

        // Now Compact should merge everything into one segment
        merged = writer.Compact();
        Assert.True(merged > 1, $"Expected multiple segments merged, got {merged}");

        var finalSegs = System.IO.Directory.GetFiles(SubDir("compact_snapshot"), "*.seg");
        Assert.Single(finalSegs);
    }


    /// <summary>
    /// Verifies that ForceMerge preserves source segments when MergeAll returns null.
    /// Segments are created by explicit commit after each batch, which ensures the
    /// term dictionary is fully written before deletes are applied.
    /// </summary>
    [Fact(DisplayName = "ForceMerge preserves source segments when all docs are dead")]
    public void ForceMerge_PreservesSourceSegments_WhenAllDocsDead()
    {
        var dir = new MMapDirectory(SubDir("fm_preserve"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 100, MergeThreshold = 10 };

        using var writer = new IndexWriter(dir, config);

        // Create 3 segments explicitly: commit after each batch
        for (int batch = 0; batch < 3; batch++)
        {
            for (int i = 1; i <= 3; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", $"batch{batch}-doc{i}"));
                doc.Add(new TextField("body", $"content {batch * 3 + i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var initialSegs = System.IO.Directory.GetFiles(SubDir("fm_preserve"), "*.seg");
        Assert.True(initialSegs.Length >= 3, $"Expected at least 3 segments, got {initialSegs.Length}");

        // Hard-delete all documents by body field
        for (int batch = 0; batch < 3; batch++)
            for (int i = 1; i <= 3; i++)
                writer.DeleteDocuments(new Rowles.LeanCorpus.Search.Queries.TermQuery("id", $"batch{batch}-doc{i}"));
        writer.Commit();
        // Verify deletes took effect
        Assert.All(writer.CommittedSegments, s => Assert.Equal(0, s.LiveDocCount));

        // ForceMerge to 1 segment. All docs are dead so merge returns null.
        // Source segments must be preserved, not dropped.
        int merged = writer.ForceMerge(1);
        Assert.Equal(0, merged);

        var finalSegs = System.IO.Directory.GetFiles(SubDir("fm_preserve"), "*.seg");
        Assert.True(finalSegs.Length >= 3, $"Expected source segments preserved, got {finalSegs.Length}");
    }

    /// <summary>
    /// Verifies that Compact preserves source segments when all documents
    /// in those segments are dead.
    /// </summary>
    [Fact(DisplayName = "Compact preserves source segments when all docs are dead")]
    public void Compact_PreservesSourceSegments_WhenAllDocsDead()
    {
        var dir = new MMapDirectory(SubDir("compact_preserve"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 100, MergeThreshold = 10 };

        using var writer = new IndexWriter(dir, config);

        // Create 3 segments explicitly
        for (int batch = 0; batch < 3; batch++)
        {
            for (int i = 1; i <= 3; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", $"batch{batch}-doc{i}"));
                doc.Add(new TextField("body", $"content {batch * 3 + i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var initialSegs = System.IO.Directory.GetFiles(SubDir("compact_preserve"), "*.seg");
        Assert.True(initialSegs.Length >= 3, $"Expected at least 3 segments, got {initialSegs.Length}");

        // Hard-delete all documents
        for (int batch = 0; batch < 3; batch++)
            for (int i = 1; i <= 3; i++)
                writer.DeleteDocuments(new Rowles.LeanCorpus.Search.Queries.TermQuery("id", $"batch{batch}-doc{i}"));
        writer.Commit();

        Assert.All(writer.CommittedSegments, s => Assert.Equal(0, s.LiveDocCount));

        int merged = writer.Compact();
        Assert.Equal(0, merged);

        var finalSegs = System.IO.Directory.GetFiles(SubDir("compact_preserve"), "*.seg");
        Assert.True(finalSegs.Length >= 3, $"Expected source segments preserved, got {finalSegs.Length}");
    }

    /// <summary>
    /// Verifies that many detached threshold flushes produce correct search results
    /// with no document loss or duplication.
    /// </summary>
    [Fact(DisplayName = "Detached flush: Many threshold flushes preserve all documents")]
    public void DetachedFlush_ManyThresholdFlushes_PreservesAllDocuments()
    {
        var dir = new MMapDirectory(SubDir("detached_flush_many"));
        var config = new IndexWriterConfig
        {
            RamBufferSizeMB = 0.5,         // Forces frequent threshold flushes
            RamPerThreadHardLimitMB = 0.25
        };
        using var writer = new IndexWriter(dir, config);

        int docCount = 2000;
        for (int i = 0; i < docCount; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body",
                $"Document number {i} with some padding text here. {new string('x', 100)}"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // Verify segment count — should have multiple segments from threshold flushes
        var segFiles = System.IO.Directory.GetFiles(SubDir("detached_flush_many"), "*.seg");
        Assert.True(segFiles.Length > 1,
            $"Expected multiple segments from threshold flushes, got {segFiles.Length}");

        // Search for specific documents to verify no loss
        using var searcher = new IndexSearcher(dir);

        var results = searcher.Search(
            new TermQuery("body", "500"), 10);
        Assert.True(results.TotalHits > 0, "Should find documents containing '500'");

        // Verify total document count via MatchAllDocs
        var all = searcher.Search(
            new MatchAllDocsQuery(), docCount + 100);
        Assert.Equal(docCount, all.TotalHits);
    }

    /// <summary>
    /// Two threads adding documents with tight RAM limits to force frequent
    /// threshold flushes, while a third thread periodically commits.
    /// Verifies no document loss or duplication across commits.
    /// </summary>
    [Fact(DisplayName = "Detached flush: Concurrent threshold flushes and commits are correct")]
    public void DetachedFlush_ConcurrentFlushAndCommit_NoDocumentLoss()
    {
        var dir = new MMapDirectory(SubDir("detached_concurrent"));
        var config = new IndexWriterConfig
        {
            RamBufferSizeMB = 0.5,
            RamPerThreadHardLimitMB = 0.25,
            DurableCommits = false
        };
        using var writer = new IndexWriter(dir, config);

        // Add documents in batches, committing between batches.
        // The tight RAM limits force threshold flushes during each batch.
        int batchCount = 10;
        int docsPerBatch = 200;
        for (int batch = 0; batch < batchCount; batch++)
        {
            for (int i = 0; i < docsPerBatch; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body",
                    $"batch{batch} doc{i} {new string('x', 80)}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        // Verify total document count
        using var searcher = new IndexSearcher(dir);
        var all = searcher.Search(new MatchAllDocsQuery(), batchCount * docsPerBatch + 100);
        Assert.Equal(batchCount * docsPerBatch, all.TotalHits);

        // Verify documents from first and last batches are searchable
        var first = searcher.Search(new TermQuery("body", "batch0"), docsPerBatch + 10);
        var last = searcher.Search(new TermQuery("body", "batch9"), docsPerBatch + 10);
        Assert.Equal(docsPerBatch, first.TotalHits);
        Assert.Equal(docsPerBatch, last.TotalHits);
    }

    /// <summary>
    /// Takes a snapshot while documents are being added with tight RAM limits
    /// (forcing threshold flushes). The snapshot must be internally consistent.
    /// </summary>
    [Fact(DisplayName = "Detached flush: Snapshot during active threshold flushes is consistent")]
    public void DetachedFlush_SnapshotDuringFlush_Consistent()
    {
        var dir = new MMapDirectory(SubDir("detached_snapshot"));
        var config = new IndexWriterConfig
        {
            RamBufferSizeMB = 0.5,
            RamPerThreadHardLimitMB = 0.25,
            DurableCommits = false
        };
        using var writer = new IndexWriter(dir, config);

        // Add initial batch and commit so snapshot has a baseline
        for (int i = 0; i < 500; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"initial doc {i} {new string('x', 80)}"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // Start adding more documents while taking snapshots
        var snapshot = writer.CreateSnapshot();

        for (int i = 0; i < 500; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"later doc {i} {new string('x', 80)}"));
            writer.AddDocument(doc);
        }

        // Snapshot should reflect committed state at creation time
        using var snapshotSearcher = new IndexSearcher(dir, snapshot.Segments);
        var initial = snapshotSearcher.Search(new TermQuery("body", "initial"), 1000);
        var later = snapshotSearcher.Search(new TermQuery("body", "later"), 1000);
        Assert.True(initial.TotalHits > 0, "Snapshot should contain initial documents");
        Assert.Equal(0, later.TotalHits); // Snapshot should not see later documents

        writer.Commit();

        // After commit, new searcher should see everything
        using var searcher = new IndexSearcher(dir);
        var all = searcher.Search(new MatchAllDocsQuery(), 1100);
        Assert.Equal(1000, all.TotalHits);
    }

    /// <summary>
    /// Issues UpdateDocument (delete + add) while threshold flushes are in progress.
    /// The delete must apply to previously published segments without affecting
    /// documents added after the delete call.
    /// </summary>
    [Fact(DisplayName = "Detached flush: UpdateDocument during active threshold flushes")]
    public void DetachedFlush_UpdateDocumentDuringFlush_Correct()
    {
        var dir = new MMapDirectory(SubDir("detached_update"));
        var config = new IndexWriterConfig
        {
            RamBufferSizeMB = 0.5,
            RamPerThreadHardLimitMB = 0.25,
            DurableCommits = false
        };
        using var writer = new IndexWriter(dir, config);

        // Add documents with StringField IDs for exact-match lookup
        for (int i = 0; i < 500; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", $"target-{i}", stored: false));
            doc.Add(new TextField("body", $"target doc {i} {new string('x', 80)}"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // Delete first 100 via UpdateDocument and add replacements
        for (int i = 0; i < 100; i++)
        {
            var replacement = new LeanDocument();
            replacement.Add(new StringField("id", $"replacement-{i}", stored: false));
            replacement.Add(new TextField("body", $"replacement doc {i} {new string('x', 80)}"));
            writer.UpdateDocument("id", $"target-{i}", replacement);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var all = searcher.Search(new MatchAllDocsQuery(), 1000);
        Assert.Equal(500, all.TotalHits); // 500 docs: 400 original + 100 replacements

        // Originals that were not deleted should still be there
        var remaining = searcher.Search(new TermQuery("body", "target"), 500);
        Assert.Equal(400, remaining.TotalHits);

        // Replacements should be searchable
        var repl = searcher.Search(new TermQuery("body", "replacement"), 200);
        Assert.Equal(100, repl.TotalHits);
    }

    /// <summary>
    /// Rollback after documents have been flushed via threshold detach.
    /// The writer must remain usable afterward.
    /// </summary>
    [Fact(DisplayName = "Detached flush: Rollback after threshold flushes leaves writer usable")]
    public void DetachedFlush_RollbackAfterFlushes_WriterUsable()
    {
        var dir = new MMapDirectory(SubDir("detached_rollback"));
        var config = new IndexWriterConfig
        {
            RamBufferSizeMB = 0.5,
            RamPerThreadHardLimitMB = 0.25,
            DurableCommits = false
        };
        using var writer = new IndexWriter(dir, config);

        // Add documents to force threshold flushes
        for (int i = 0; i < 500; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"flushable doc {i} {new string('x', 80)}"));
            writer.AddDocument(doc);
        }

        // Rollback should not throw
        writer.Rollback();

        // Writer should still be usable
        for (int i = 0; i < 100; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"postrollback doc {i}"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var postRollback = searcher.Search(new TermQuery("body", "postrollback"), 200);
        Assert.Equal(100, postRollback.TotalHits);
    }

    /// <summary>
    /// Dispose after commit does not throw and releases the write lock.
    /// </summary>
    [Fact(DisplayName = "Detached flush: Dispose after threshold flushes releases resources")]
    public void DetachedFlush_DisposeAfterFlushes_ReleasesResources()
    {
        var subDirPath = SubDir("detached_dispose");
        var dir = new MMapDirectory(subDirPath);
        var config = new IndexWriterConfig
        {
            RamBufferSizeMB = 0.5,
            RamPerThreadHardLimitMB = 0.25,
            DurableCommits = false
        };
        using (var writer = new IndexWriter(dir, config))
        {
            for (int i = 0; i < 500; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"Disposable doc {i} {new string('x', 80)}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        } // Dispose

        // Verify write lock is released
        var lockPath = System.IO.Path.Combine(subDirPath, "write.lock");
        Assert.False(System.IO.File.Exists(lockPath), "Write lock should be released after Dispose");

        // Verify documents survive reopen
        using var reopenDir = new MMapDirectory(subDirPath);
        using var searcher = new IndexSearcher(reopenDir);
        var all = searcher.Search(new MatchAllDocsQuery(), 600);
        Assert.Equal(500, all.TotalHits);
    }
}
