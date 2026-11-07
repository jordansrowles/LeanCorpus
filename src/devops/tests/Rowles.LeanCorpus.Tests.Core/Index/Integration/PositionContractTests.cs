using System.Text.Json.Nodes;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Verifies the segment position contract marker: flush stamps it, merges and imports keep it only when every
/// source has it, and migration, copying and recovery never add it.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class PositionContractTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public PositionContractTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string NewDirectory(string name)
    {
        var path = Path.Combine(_fixture.Path, name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static IndexWriterConfig Config() => new() { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance };

    private static void AddDocument(string path, string id)
    {
        using var writer = new IndexWriter(new MMapDirectory(path), Config());
        var doc = new LeanDocument();
        doc.Add(new StringField("id", id));
        doc.Add(new TextField("body", "said the government"));
        writer.AddDocument(doc);
        writer.Commit();
    }

    private static Dictionary<string, bool> Markers(string path)
        => IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)?.SegmentInfos
            .ToDictionary(static info => info.SegmentId, static info => info.HasPositionContract, StringComparer.Ordinal)
            ?? new Dictionary<string, bool>(StringComparer.Ordinal);

    /// <summary>Rewrites a segment's metadata as a pre-contract writer produced it: without the property.</summary>
    private static void MakeLegacy(string path, string segmentId)
    {
        string file = Path.Combine(path, segmentId + ".seg");
        var node = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        node.Remove("PositionContract");
        File.WriteAllText(file, node.ToJsonString());
    }

    private string BuildSource(string name, int segments, int legacyIndex = -1)
    {
        string path = NewDirectory(name);
        for (int i = 0; i < segments; i++)
            AddDocument(path, name + i);
        if (legacyIndex >= 0)
            MakeLegacy(path, Markers(path).Keys.Order(StringComparer.Ordinal).ElementAt(legacyIndex));
        return path;
    }

    [Fact(DisplayName = "SegmentInfo: the position contract round-trips and DeepCopy keeps it")]
    public void SegmentInfo_RoundTripsAndCopiesTheContract()
    {
        string file = Path.Combine(NewDirectory("roundtrip"), "seg_0.seg");
        var info = new SegmentInfo { SegmentId = "seg_0", DocCount = 1, LiveDocCount = 1, PositionContract = SegmentInfo.CurrentPositionContract };

        info.WriteTo(file);
        var read = SegmentInfo.ReadFrom(file);

        Assert.Equal(SegmentInfo.CurrentPositionContract, read.PositionContract);
        Assert.True(read.HasPositionContract);
        Assert.Equal(SegmentInfo.CurrentPositionContract, read.DeepCopy().PositionContract);
    }

    [Fact(DisplayName = "SegmentInfo: metadata without the property reads as legacy")]
    public void SegmentInfo_WithoutTheProperty_ReadsAsLegacy()
    {
        string file = Path.Combine(NewDirectory("legacy"), "seg_0.seg");
        new SegmentInfo { SegmentId = "seg_0", DocCount = 1, LiveDocCount = 1, PositionContract = 1 }.WriteTo(file);
        var node = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        node.Remove("PositionContract");
        File.WriteAllText(file, node.ToJsonString());

        var read = SegmentInfo.ReadFrom(file);

        Assert.Null(read.PositionContract);
        Assert.False(read.HasPositionContract);
        Assert.Null(read.DeepCopy().PositionContract);
    }

    [Theory(DisplayName = "SegmentInfo: a non-positive contract is invalid metadata")]
    [InlineData(0)]
    [InlineData(-1)]
    public void SegmentInfo_RejectsANonPositiveContract(int value)
    {
        string file = Path.Combine(NewDirectory("invalid"), "seg_0.seg");
        new SegmentInfo { SegmentId = "seg_0", DocCount = 1, LiveDocCount = 1, PositionContract = value }.WriteTo(file);

        Assert.Throws<InvalidDataException>(() => SegmentInfo.ReadFrom(file));
    }

    [Fact(DisplayName = "Flush: every flushed segment is marked")]
    public void Flush_StampsTheContract()
    {
        string path = BuildSource("flush", 3);

        Assert.All(Markers(path), pair => Assert.True(pair.Value, pair.Key));
        Assert.Equal(3, Markers(path).Count);
    }

    [Theory(DisplayName = "Merge: the merged segment is marked only when every source is")]
    [InlineData(3, -1, true)]
    [InlineData(3, 0, false)]
    [InlineData(3, 2, false)]
    [InlineData(2, 1, false)]
    public void Merge_IsMarkedOnlyWhenEverySourceIs(int segments, int legacyIndex, bool expectedMarked)
    {
        string path = BuildSource("merge", segments, legacyIndex);
        using (var writer = new IndexWriter(new MMapDirectory(path), Config()))
        {
            writer.ForceMerge(1);
            writer.Commit();
        }

        var merged = Assert.Single(Markers(path));
        Assert.Equal(expectedMarked, merged.Value);
    }

    [Theory(DisplayName = "AddIndexes: the imported segment is marked only when every source segment is")]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void AddIndexes_IsMarkedOnlyWhenEverySourceIs(int legacyIndex, bool expectedMarked)
    {
        string target = BuildSource("target", 2);
        string source = BuildSource("source", 2, legacyIndex);
        var before = Markers(target);

        using (var writer = new IndexWriter(new MMapDirectory(target), Config()))
        {
            writer.AddIndexes(new MMapDirectory(source));
            writer.Commit();
        }

        var after = Markers(target);
        foreach (var (id, marked) in before)
            Assert.Equal(marked, after[id]);
        var added = Assert.Single(after.Where(pair => !before.ContainsKey(pair.Key)));
        Assert.Equal(expectedMarked, added.Value);
    }

    [Fact(DisplayName = "Recovery and reopen keep each segment's marker")]
    public void Recovery_KeepsEachMarker()
    {
        string path = BuildSource("recovery", 3, legacyIndex: 1);
        var expected = Markers(path);

        using (var searcher = new IndexSearcher(new MMapDirectory(path)))
            Assert.Equal(3, searcher.Search(new TermQuery("body", "government"), 10, TestContext.Current.CancellationToken).TotalHits);

        using (var writer = new IndexWriter(new MMapDirectory(path), Config()))
            writer.Commit();

        Assert.Equal(expected.OrderBy(static pair => pair.Key, StringComparer.Ordinal),
            Markers(path).OrderBy(static pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(2, expected.Count(static pair => pair.Value));
    }

    [Theory(DisplayName = "Lifecycle: a segment is never marked unless all its content was written under the contract")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Lifecycle_NeverMarksASegmentWithLegacyContent(int seed)
    {
        var random = new Random(seed);
        string path = NewDirectory("lifecycle");
        int documents = 0;

        for (int step = 0; step < 14; step++)
        {
            var before = Markers(path);
            switch (random.Next(5))
            {
                case 0:
                case 1:
                {
                    AddDocument(path, "d" + documents++);
                    var after = Markers(path);
                    var added = Assert.Single(after.Where(pair => !before.ContainsKey(pair.Key)));
                    Assert.True(added.Value, "A flushed segment must be marked.");
                    AssertUnchanged(before, after);
                    break;
                }
                case 2:
                {
                    AddDocument(path, "d" + documents++);
                    string added = Assert.Single(Markers(path).Keys.Except(before.Keys));
                    MakeLegacy(path, added);
                    Assert.False(Markers(path)[added]);
                    break;
                }
                case 3:
                {
                    using (var writer = new IndexWriter(new MMapDirectory(path), Config()))
                    {
                        writer.ForceMerge(1);
                        writer.Commit();
                    }

                    var after = Markers(path);
                    if (before.Count >= 2)
                    {
                        var merged = Assert.Single(after);
                        Assert.Equal(before.Values.All(static marked => marked), merged.Value);
                    }
                    else
                    {
                        Assert.Equal(before.OrderBy(static pair => pair.Key, StringComparer.Ordinal),
                            after.OrderBy(static pair => pair.Key, StringComparer.Ordinal));
                    }

                    break;
                }
                default:
                {
                    int legacyIndex = random.Next(3) == 0 ? random.Next(2) : -1;
                    string source = BuildSource("import" + step, 2, legacyIndex);
                    using (var writer = new IndexWriter(new MMapDirectory(path), Config()))
                    {
                        writer.AddIndexes(new MMapDirectory(source));
                        writer.Commit();
                    }

                    var after = Markers(path);
                    var added = Assert.Single(after.Where(pair => !before.ContainsKey(pair.Key)));
                    Assert.Equal(legacyIndex < 0, added.Value);
                    AssertUnchanged(before, after);
                    break;
                }
            }
        }
    }

    private static void AssertUnchanged(Dictionary<string, bool> before, Dictionary<string, bool> after)
    {
        foreach (var (id, marked) in before)
            Assert.Equal(marked, after[id]);
    }
}
