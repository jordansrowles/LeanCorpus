using static Rowles.LeanCorpus.Tests.Core.Codecs.VectorGraphTestData;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

/// <summary>Round-trip and fail-closed validation tests for the <c>.vgr</c> body (ADR052).</summary>
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class VectorGraphPersistenceTests : IClassFixture<TestDirectoryFixture>
{
    private static readonly HnswBuildConfig Config = new() { M = 8, EfConstruction = 60 };
    private readonly TestDirectoryFixture _fixture;

    public VectorGraphPersistenceTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Theory(DisplayName = "Roundtrip: Preserves Graph And Search Results")]
    [InlineData(true)]
    [InlineData(false)]
    public void Roundtrip_PreservesGraphAndSearchResults(bool normalised)
    {
        var raw = Sparse(Clustered(600, 12, clusters: 8, spread: 0.2f, seed: 1));
        var vectors = normalised ? NormaliseAll(raw) : raw;
        using var built = Build(vectors, Config, seed: 17, normalised);
        string path = Write(built, $"roundtrip_{normalised}.vgr");

        using var read = Open(path, vectors, normalised);
        Assert.Equal(built.DocIds.ToArray(), read.DocIds.ToArray());
        Assert.Equal(built.CopyLevel0(), read.CopyLevel0());
        Assert.Equal(built.MaxLevel, read.MaxLevel);
        Assert.Equal(built.EntryOrdinal, read.EntryOrdinal);
        Assert.Equal(built.Seed, read.Seed);
        Assert.Equal(built.InverseNorms.ToArray(), read.InverseNorms.ToArray());
        for (int level = 1; level <= built.MaxLevel; level++)
        {
            Assert.Equal(built.LevelOrdinals(level).ToArray(), read.LevelOrdinals(level).ToArray());
            Assert.Equal(built.UpperAdjacency(level).ToArray(), read.UpperAdjacency(level).ToArray());
        }

        var source = new ArrayVectorSource(vectors);
        var query = Normalise(vectors.First(static v => v is not null));
        var expected = new List<VectorGraphHit>();
        var actual = new List<VectorGraphHit>();
        built.Search(query, source, 10, 40, null, long.MaxValue, expected);
        read.Search(query, source, 10, 40, null, long.MaxValue, actual);
        Assert.Equal(expected, actual);
    }

    [Fact(DisplayName = "Roundtrip: Same Seed Writes Byte-Identical Files")]
    public void Roundtrip_SameSeed_WritesByteIdenticalFiles()
    {
        var vectors = NormaliseAll(Clustered(800, 16, clusters: 10, spread: 0.2f, seed: 2));
        using var first = Build(vectors, Config, seed: 5, normalised: true);
        using var second = Build(vectors, Config, seed: 5, normalised: true);

        Assert.Equal(File.ReadAllBytes(Write(first, "same_a.vgr")), File.ReadAllBytes(Write(second, "same_b.vgr")));
    }

    [Fact(DisplayName = "Roundtrip: Empty Graph")]
    public void Roundtrip_EmptyGraph()
    {
        using var empty = VectorGraph.FromArrays(4, true, 8, 16, 60, 1, -1, -1, [], [], [], [], null);
        string path = Write(empty, "empty.vgr");
        using var read = VectorGraphReader.Read(new IndexInput(path), new VectorGraphReader.Expectation(4, true, 5, _ => false));
        Assert.Equal(0, read.NodeCount);
    }

    [Fact(DisplayName = "Corruption: Bad Checksum Is Rejected")]
    public void Corruption_BadChecksum_IsRejected()
    {
        var vectors = NormaliseAll(Clustered(200, 8, clusters: 4, spread: 0.2f, seed: 3));
        using var graph = Build(vectors, Config, seed: 1, normalised: true);
        string path = Write(graph, "checksum.vgr");
        byte[] bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 0x5A;
        File.WriteAllBytes(path, bytes);

        Assert.ThrowsAny<Exception>(() => Open(path, vectors, normalised: true).Dispose());
    }

    [Fact(DisplayName = "Corruption: A Structurally Valid Body Change Fails The Checksum")]
    public void Corruption_StructurallyValidBodyChange_FailsTheChecksum()
    {
        // Lowering one inverse norm's least significant mantissa bit keeps it finite and
        // non-negative, so only the checksum can reject the file.
        var vectors = Clustered(200, 8, clusters: 4, spread: 0.2f, seed: 9);
        using var graph = Build(vectors, Config, seed: 1, normalised: false);
        string path = Write(graph, "silent.vgr");
        byte[] bytes = File.ReadAllBytes(path);
        byte[] last = BitConverter.GetBytes(graph.InverseNorms[^1]);
        int offset = bytes.AsSpan().LastIndexOf(last);
        Assert.True(offset > 0, "The last inverse norm was not found in the file.");
        bytes[offset] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        var present = new HashSet<int>(Enumerable.Range(0, vectors.Length));
        var error = Assert.Throws<CodecFileException>(() => VectorGraphReader.Read(new IndexInput(path),
            new VectorGraphReader.Expectation(8, false, vectors.Length, present.Contains)).Dispose());
        Assert.Equal(CodecFileErrorCode.ChecksumMismatch, error.ErrorCode);
    }

    [Fact(DisplayName = "Build: Oversized Graphs Are Refused Before Allocation")]
    public void Build_OversizedGraphs_AreRefusedBeforeAllocation()
    {
        Assert.True(VectorGraphBuilder.CanBuild(1_000_000, Config));
        Assert.False(VectorGraphBuilder.CanBuild(int.MaxValue / 4, new HnswBuildConfig { M = 16 }));
        Assert.False(VectorGraphBuilder.CanBuild(20_000_000, new HnswBuildConfig { M = 100 }));
    }

    [Fact(DisplayName = "Scratch: Large Graphs Borrow From Their Own Pool")]
    public void Scratch_LargeGraphsBorrowFromTheirOwnPool()
    {
        var pool = new VectorGraphScratchPool();
        var first = VectorGraphSearchScratch.Borrow(VectorGraphSearchScratch.ThreadCacheMaxNodes + 1, pool);
        first.Dispose();
        var second = VectorGraphSearchScratch.Borrow(VectorGraphSearchScratch.ThreadCacheMaxNodes + 1, pool);
        Assert.Same(first, second);
        second.Dispose();

        using var small = VectorGraphSearchScratch.Borrow(16, pool);
        Assert.NotSame(first, small);
        pool.Clear();
    }

    [Fact(DisplayName = "Corruption: Truncated File Is Rejected")]
    public void Corruption_TruncatedFile_IsRejected()
    {
        var vectors = NormaliseAll(Clustered(200, 8, clusters: 4, spread: 0.2f, seed: 4));
        using var graph = Build(vectors, Config, seed: 1, normalised: true);
        string path = Write(graph, "truncated.vgr");
        byte[] bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length - 40)]);

        Assert.ThrowsAny<Exception>(() => Open(path, vectors, normalised: true).Dispose());
    }

    [Fact(DisplayName = "Corruption: Mismatched Field Contract Is Rejected")]
    public void Corruption_MismatchedFieldContract_IsRejected()
    {
        var vectors = NormaliseAll(Clustered(200, 8, clusters: 4, spread: 0.2f, seed: 5));
        using var graph = Build(vectors, Config, seed: 1, normalised: true);
        string path = Write(graph, "contract.vgr");
        var present = new HashSet<int>(Enumerable.Range(0, vectors.Length));

        AssertInvalid(path, new(Dimension: 9, Normalised: true, vectors.Length, present.Contains), "dimension");
        AssertInvalid(path, new(Dimension: 8, Normalised: false, vectors.Length, present.Contains), "Normalised");
        AssertInvalid(path, new(Dimension: 8, Normalised: true, vectors.Length, docId => docId != 10), "no stored vector");
        AssertInvalid(path, new(Dimension: 8, Normalised: true, vectors.Length + 1, docId => docId <= vectors.Length), "stored vectors");
    }

    [Fact(DisplayName = "Corruption: Structural Violations Are Rejected")]
    public void Corruption_StructuralViolations_AreRejected()
    {
        var vectors = NormaliseAll(Clustered(300, 8, clusters: 4, spread: 0.2f, seed: 6));
        using var graph = Build(vectors, Config, seed: 3, normalised: true);
        Assert.True(graph.MaxLevel >= 1, "The fixture needs an upper level.");
        var present = new HashSet<int>(Enumerable.Range(0, vectors.Length));
        var expectation = new VectorGraphReader.Expectation(8, true, vectors.Length, present.Contains);
        int stride0 = 1 + graph.M0;

        int[] outOfRange = graph.CopyLevel0();
        outOfRange[1] = graph.NodeCount;
        AssertInvalid(Write(With(graph, level0: outOfRange), "range.vgr"), expectation, "invalid neighbour ordinal");

        int[] selfEdge = graph.CopyLevel0();
        selfEdge[stride0 + 1] = 1;
        AssertInvalid(Write(With(graph, level0: selfEdge), "self.vgr"), expectation, "invalid neighbour ordinal");

        int[] badCount = graph.CopyLevel0();
        badCount[0] = graph.M0 + 1;
        AssertInvalid(Write(With(graph, level0: badCount), "count.vgr"), expectation, "neighbour count");

        int[] dirtySlot = graph.CopyLevel0();
        int ordinal = Enumerable.Range(0, graph.NodeCount).First(o => dirtySlot[o * stride0] < graph.M0);
        dirtySlot[ordinal * stride0 + graph.M0] = 0 == ordinal ? 1 : 0;
        AssertInvalid(Write(With(graph, level0: dirtySlot), "slot.vgr"), expectation, "unused neighbour slot");

        int[] unsorted = graph.DocIds.ToArray();
        (unsorted[3], unsorted[4]) = (unsorted[4], unsorted[3]);
        AssertInvalid(Write(With(graph, docIds: unsorted), "unsorted.vgr"), expectation, "strictly ascending");

        // Remove every in-edge of one node: each record stays valid, but the node is unreachable.
        int stranded = Enumerable.Range(0, graph.NodeCount).First(o => o != graph.EntryOrdinal);
        int[] unreachable = graph.CopyLevel0();
        for (int o = 0; o < graph.NodeCount; o++)
        {
            Span<int> record = unreachable.AsSpan(o * stride0, stride0);
            var kept = record.Slice(1, record[0]).ToArray().Where(n => n != stranded).ToArray();
            record.Fill(-1);
            record[0] = kept.Length;
            kept.CopyTo(record[1..]);
        }
        AssertInvalid(Write(With(graph, level0: unreachable), "unreachable.vgr"), expectation, "unreachable from the entry point");

        int notTop = Enumerable.Range(0, graph.NodeCount).First(o => Array.BinarySearch(graph.LevelOrdinals(graph.MaxLevel).ToArray(), o) < 0);
        AssertInvalid(Write(With(graph, entry: notTop), "entry.vgr"), expectation, "entry ordinal");
    }

    [Fact(DisplayName = "Corruption: Invalid Inverse Norms And Trailing Bytes Are Rejected")]
    public void Corruption_InvalidInverseNormsAndTrailingBytes_AreRejected()
    {
        var vectors = Clustered(200, 8, clusters: 4, spread: 0.2f, seed: 7);
        using var graph = Build(vectors, Config, seed: 2, normalised: false);
        var present = new HashSet<int>(Enumerable.Range(0, vectors.Length));
        var expectation = new VectorGraphReader.Expectation(8, false, vectors.Length, present.Contains);

        float[] norms = graph.InverseNorms.ToArray();
        norms[9] = float.NaN;
        AssertInvalid(Write(With(graph, inverseNorms: norms), "norms.vgr"), expectation, "inverse norm");
        norms[9] = 1e16f; // above the conditioned range: the vector would need the scaled cosine
        AssertInvalid(Write(With(graph, inverseNorms: norms), "unconditioned.vgr"), expectation, "inverse norm");
        norms[9] = -0.5f;
        AssertInvalid(Write(With(graph, inverseNorms: norms), "negative.vgr"), expectation, "inverse norm");
        norms[9] = VectorGraph.ScaledCosine;
        VectorGraphReader.Read(new IndexInput(Write(With(graph, inverseNorms: norms), "scaled.vgr")), expectation).Dispose();

        string trailing = Path.Combine(_fixture.Path, "trailing.vgr");
        CodecFileWriter.WriteAtomically(trailing, VectorCodecFiles.Graph, durable: false, body =>
        {
            VectorGraphWriter.WriteBody(body, graph);
            body.WriteInt32(0);
        });
        AssertInvalid(trailing, expectation, "trailing body bytes");
    }

    private static float[][] Sparse(float[][] vectors)
    {
        for (int i = 0; i < vectors.Length; i += 7)
            vectors[i] = null!;
        return vectors;
    }

    private string Write(VectorGraph graph, string name)
    {
        string path = Path.Combine(_fixture.Path, name);
        VectorGraphWriter.Write(path, graph);
        return path;
    }

    private static VectorGraph Open(string path, float[][] vectors, bool normalised)
        => VectorGraphReader.Read(new IndexInput(path),
            new VectorGraphReader.Expectation(vectors.First(static v => v is not null).Length, normalised, vectors.Length,
                docId => vectors[docId] is not null));

    private static void AssertInvalid(string path, VectorGraphReader.Expectation expectation, string message)
    {
        var error = Assert.Throws<InvalidDataException>(() => VectorGraphReader.Read(new IndexInput(path), expectation).Dispose());
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    private static VectorGraph With(
        VectorGraph graph, int[]? level0 = null, int[]? docIds = null, int? entry = null, float[]? inverseNorms = null)
    {
        var upperOrdinals = new int[graph.MaxLevel][];
        var upperAdjacency = new int[graph.MaxLevel][];
        for (int level = 1; level <= graph.MaxLevel; level++)
        {
            upperOrdinals[level - 1] = graph.LevelOrdinals(level).ToArray();
            upperAdjacency[level - 1] = graph.UpperAdjacency(level).ToArray();
        }

        return VectorGraph.FromArrays(graph.Dimension, graph.Normalised, graph.M, graph.M0, graph.EfConstruction,
            graph.Seed, graph.MaxLevel, entry ?? graph.EntryOrdinal, docIds ?? graph.DocIds.ToArray(),
            level0 ?? graph.CopyLevel0(), upperOrdinals, upperAdjacency,
            graph.Normalised ? null : inverseNorms ?? graph.InverseNorms.ToArray());
    }
}
