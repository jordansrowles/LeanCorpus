using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rowles.LeanCorpus.Tests.Core.Index.Migration;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
[Area(TestArea.Search)]
[Area(TestArea.CodecKit)]
public sealed class Release311MigrationAcceptanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"release-311-{Guid.NewGuid():N}");

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FrozenPackageCorpusMigratesReopensAndMerges(bool compound)
    {
        string source = Path.Combine(_root, "source");
        JsonElement manifest = Release311Fixture.Extract(compound, source);
        CorpusSnapshot before = AssertCorpus(source, manifest, merged: false);
        AssertLegacyVectorVersionPolicy(source, accepts: true);
        byte[][] originalGraphs = CaptureHnswBodies(source);
        using (var directory = new MMapDirectory(source))
        {
            var compatibility = IndexCompatibility.Check(directory);
            Assert.Equal(IndexCompatibilityStatus.MigrationRecommended, compatibility.Status);
            Assert.False(compatibility.MustReject);
            Assert.False(compatibility.RequiresMigration);
            Assert.True(compatibility.CanRead);
            Assert.True(compatibility.CanValidate);
            Assert.True(compatibility.CanMigrate);
            Assert.False(compatibility.CanWrite);
            AssertMigrationActions(compatibility.MigrationActions, manifest);
            AssertPlan(IndexCodecMigrator.Plan(directory), manifest);
            var dryRun = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = true });
            Assert.True(dryRun.Succeeded, Issues(dryRun));
            Assert.True(dryRun.DryRun);
        }
        Release311Fixture.VerifyFiles(manifest, source);

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        using (var directory = new MMapDirectory(target))
        {
            var migration = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false });
            Assert.True(migration.Succeeded, Issues(migration));
        }
        AssertSnapshotEqual(before, AssertCorpus(target, manifest, merged: false));
        AssertLegacyVectorVersionPolicy(target, accepts: false);
        byte[][] migratedGraphs = CaptureHnswBodies(target);
        Assert.Equal(originalGraphs.Length, migratedGraphs.Length);
        for (int segment = 0; segment < originalGraphs.Length; segment++)
            Assert.Equal(originalGraphs[segment], migratedGraphs[segment]);
        AssertCurrentFormats(target, manifest);
        using (var directory = new MMapDirectory(target))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            IndexSort = new IndexSort(SortField.String("sort:id")),
            UseCompoundFile = compound,
            BuildHnswOnFlush = true,
            HnswSeed = 311400,
            NormaliseVectors = false,
            MergePolicy = NoMergePolicy.Instance,
        }))
        {
            writer.ForceMerge(1);
            writer.Commit();
        }
        AssertSnapshotEqual(before, AssertCorpus(target, manifest, merged: true), merged: true);
        AssertCurrentFormats(target, manifest);
        Release311Fixture.VerifyFiles(manifest, source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixtureVerifierRejectsChangedAndUnexpectedFiles(bool compound)
    {
        string path = Path.Combine(_root, "fixture");
        JsonElement manifest = Release311Fixture.Extract(compound, path);
        string extra = Path.Combine(path, "unexpected");
        File.WriteAllText(extra, "unexpected");
        Assert.Throws<InvalidDataException>(() => Release311Fixture.VerifyFiles(manifest, path));
        File.Delete(extra);
        string name = manifest.GetProperty("Files")[0].GetProperty("Name").GetString()!;
        byte[] bytes = File.ReadAllBytes(Path.Combine(path, name));
        bytes[0] ^= 1;
        File.WriteAllBytes(Path.Combine(path, name), bytes);
        Assert.Throws<InvalidDataException>(() => Release311Fixture.VerifyFiles(manifest, path));
    }

    [Fact]
    public void PublicationFailureReopensOriginalCommitAndRetryMigrates()
    {
        string path = Path.Combine(_root, "restart");
        JsonElement manifest = Release311Fixture.Extract(false, path);
        CorpusSnapshot before = AssertCorpus(path, manifest, merged: false);
        var originals = Directory.GetFiles(path).ToDictionary(file => Path.GetFileName(file), File.ReadAllBytes);
        long generation = Assert.Single(IndexFileInspector.FindCommitFiles(path)).Generation;
        string blockedCommit = Path.Combine(path, $"segments_{generation + 1}");
        Directory.CreateDirectory(blockedCommit);
        using (var directory = new MMapDirectory(path))
        {
            AssertPlan(IndexCodecMigrator.Plan(directory), manifest);
            var failure = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false });
            Assert.False(failure.Succeeded);
        }
        // Reopen through normal recovery, with all old immutable bytes still intact.
        Assert.Equal(generation, IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.Generation);
        foreach (var file in originals)
            Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(path, file.Key!)));
        var marker = IndexMigrationRecovery.GetState(path);
        Assert.Equal(IndexMigrationState.Failed, marker.State);
        Assert.True(Directory.Exists(marker.StagingDirectory));
        AssertCurrentFormats(marker.StagingDirectory, manifest);
        IndexMigrationRecovery.Abandon(path);
        Assert.Equal(IndexMigrationState.None, IndexMigrationRecovery.GetState(path).State);
        AssertSnapshotEqual(before, AssertCorpus(path, manifest, merged: false));
        Directory.Delete(blockedCommit);
        using (var directory = new MMapDirectory(path))
        {
            var retry = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false });
            Assert.True(retry.Succeeded, Issues(retry));
        }
        AssertSnapshotEqual(before, AssertCorpus(path, manifest, merged: false));
        AssertCurrentFormats(path, manifest);
    }

    private static void AssertPlan(IndexCodecMigrationPlan plan, JsonElement manifest)
    {
        Assert.True(plan.CanExecute);
        AssertMigrationActions(plan.Actions, manifest);
    }

    private static void AssertMigrationActions(IReadOnlyList<IndexCodecMigrationAction> migrationActions, JsonElement manifest)
    {
        // The planner represents compound rewrites through their owning container;
        // repacking and commit publication are execution steps, not extra plan actions.
        var expected = manifest.GetProperty("Segments").EnumerateArray().SelectMany(segment =>
        {
            string id = segment.GetProperty("SegmentId").GetString()!;
            return new[]
            {
                (SegmentId: id, FileName: id + ".fdt", FormatId: "leancorpus.stored-fields.data",
                    Kind: IndexCodecMigrationActionKind.CoordinatedRewrite, From: (byte?)3, To: (byte?)5),
                (SegmentId: id, FileName: id + ".dvs", FormatId: "leancorpus.doc-values.sorted",
                    Kind: IndexCodecMigrationActionKind.Rewrite, From: (byte?)2, To: (byte?)3),
                (SegmentId: id, FileName: VectorFilePaths.VectorFile(id, "embedding"), FormatId: "leancorpus.vectors.float32",
                    Kind: IndexCodecMigrationActionKind.Rewrite, From: (byte?)1, To: (byte?)2),
            };
        }).OrderBy(action => action.FileName, StringComparer.Ordinal).ToArray();
        var actual = migrationActions.OrderBy(action => action.FileName, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            var action = actual[i];
            Assert.Equal(expected[i], (action.SegmentId, action.FileName, action.FormatId,
                action.Kind, action.FromVersion, action.ToVersion));
            Assert.True(action.CanExecute, action.ReasonCannotExecute);
            Assert.Equal(manifest.GetProperty("IsCompound").GetBoolean() ? action.SegmentId + ".cfs" : null,
                action.CompoundFileName);
            string[] sources = action.Kind == IndexCodecMigrationActionKind.CoordinatedRewrite
                ? [action.SegmentId + ".fdt", action.SegmentId + ".fdx"] : [action.FileName!];
            Assert.Equal(sources.Order(StringComparer.Ordinal), action.SourcePaths.Order(StringComparer.Ordinal));
        }
    }

    private static CorpusSnapshot AssertCorpus(string path, JsonElement manifest, bool merged)
    {
        using var directory = new MMapDirectory(path);
        var health = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true });
        Assert.True(health.IsHealthy, string.Join("; ", health.DetailedIssues.Select(issue => issue.Message)));
        using var searcher = new IndexSearcher(directory);
        var expected = manifest.GetProperty("Documents").EnumerateArray()
            .Where(doc => !merged || doc.GetProperty("IsLive").GetBoolean())
            .ToDictionary(doc => doc.GetProperty("Id").GetString()!, StringComparer.Ordinal);
        Assert.Equal(manifest.GetProperty("LogicalDocumentCount").GetInt32() -
            (merged ? manifest.GetProperty("DeletedDocumentCount").GetInt32() : 0), expected.Count);
        Assert.Equal(manifest.GetProperty("VectorBearingDocumentIds").EnumerateArray().Select(value => value.GetInt32()),
            manifest.GetProperty("Documents").EnumerateArray()
                .Where(doc => doc.GetProperty("Vector").ValueKind != JsonValueKind.Null)
                .Select(doc => doc.GetProperty("GlobalDocId").GetInt32()));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var snapshots = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var liveSnapshots = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var segments = searcher.GetSegmentReaders();
        Assert.Equal(merged ? 1 : 2, segments.Count);
        foreach (var segment in segments)
        {
            Assert.Equal(manifest.GetProperty("IsCompound").GetBoolean(), segment.Info.IsCompoundFile);
            Assert.All(manifest.GetProperty("Segments").EnumerateArray(), expectedSegment =>
                Assert.Equal(new[] { "String:sort:id:False" }, expectedSegment.GetProperty("IndexSort")
                    .EnumerateArray().Select(field => field.GetString()).ToArray()));
            Assert.Equal(new[] { "String:sort:id:False" }, segment.Info.IndexSortFields);
            Assert.True(IndexSort.TryParseSerialisedField(Assert.Single(segment.Info.IndexSortFields), out var logicalSort));
            Assert.Equal("sort:id", logicalSort.FieldName);
            Assert.Equal(SortFieldType.String, logicalSort.Type);
            Assert.False(logicalSort.Descending);
            using ISegmentFileSource files = segment.Info.IsCompoundFile
                ? new CompoundSegmentFileSource(directory, segment.Info.SegmentId)
                : new LooseSegmentFileSource(directory, segment.Info.SegmentId);
            var sorted = SortedDocValuesReader.Read(files.OpenInput(segment.Info.SegmentId + ".dvs"), segment.MaxDoc);
            Assert.True(segment.TryGetNumericDocValuesPresence("score", out var numericPresence));
            double[] numeric = segment.GetNumericDocValues("score")!;
            var vectorIds = new List<int>();
            for (int docId = 0; docId < segment.MaxDoc; docId++)
            {
                var stored = segment.GetStoredFields(docId);
                string id = Assert.Single(stored["id"]);
                Assert.True(seen.Add(id));
                JsonElement doc = expected[id];
                Assert.Equal(doc.GetProperty("IsLive").GetBoolean(), segment.IsLive(docId));
                var expectedStored = doc.GetProperty("Stored");
                Assert.Equal(expectedStored.EnumerateObject().Count(), stored.Count);
                foreach (var field in expectedStored.EnumerateObject())
                {
                    var values = stored[field.Name];
                    Assert.Equal(field.Value.GetArrayLength(), values.Count);
                    for (int value = 0; value < values.Count; value++)
                    {
                        Assert.Equal(field.Value[value].GetProperty("Utf16Length").GetInt32(), values[value].Length);
                        Assert.Equal(field.Value[value].GetProperty("Sha256").GetString(),
                            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(values[value]))));
                    }
                }
                bool hasNumeric = doc.GetProperty("Numeric").ValueKind != JsonValueKind.Null;
                Assert.Equal(hasNumeric, numericPresence is null || numericPresence.Contains(docId));
                if (hasNumeric) Assert.Equal(doc.GetProperty("Numeric").GetDouble(), numeric[docId]);
                bool hasSorted = doc.GetProperty("Sorted").ValueKind != JsonValueKind.Null;
                Assert.Equal(hasSorted, sorted.Presence["category"] is null || sorted.Presence["category"]!.Contains(docId));
                if (hasSorted) Assert.Equal(doc.GetProperty("Sorted").GetString(), sorted.Values["category"][docId]);
                bool hasBinary = doc.GetProperty("Binary").ValueKind != JsonValueKind.Null;
                bool actualBinary = segment.TryGetBinaryDocValues("payload", docId, out var binary);
                Assert.Equal(hasBinary, actualBinary);
                if (hasBinary) Assert.Equal(doc.GetProperty("Binary").GetString(), Convert.ToHexString(Assert.Single(binary)));
                bool hasVector = doc.GetProperty("Vector").ValueKind != JsonValueKind.Null;
                Assert.Equal(hasVector, segment.HasVector("embedding", docId));
                if (hasVector)
                {
                    vectorIds.Add(docId);
                    Assert.Equal(doc.GetProperty("Vector").EnumerateArray().Select(value => value.GetSingle()).ToArray(), segment.GetVector("embedding", docId));
                }
                else Assert.Null(segment.GetVector("embedding", docId));
                string snapshot = JsonSerializer.Serialize(new
                {
                    Id = id,
                    IsLive = segment.IsLive(docId),
                    Stored = stored.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new { pair.Key, Values = pair.Value.ToArray() }).ToArray(),
                    Numeric = hasNumeric ? (double?)numeric[docId] : null,
                    Sorted = hasSorted ? sorted.Values["category"][docId] : null,
                    Binary = actualBinary ? binary.Select(Convert.ToHexString).ToArray() : null,
                    Vector = segment.GetVector("embedding", docId),
                    IndexSort = segment.Info.IndexSortFields,
                });
                snapshots.Add(id, snapshot);
                if (segment.IsLive(docId)) liveSnapshots.Add(id, snapshot);
            }
            Assert.Equal(vectorIds, segment.GetHnswGraph("embedding")!.GetNodesAtLevel(0).Order().ToArray());
        }
        Assert.Equal(expected.Keys.Order(), seen.Order());
        string[] liveIds = expected.Where(pair => pair.Value.GetProperty("IsLive").GetBoolean()).Select(pair => pair.Key).Order().ToArray();
        Assert.Equal(liveIds, SearchIds(searcher, new TermQuery("body", "migration"), 10).Order().ToArray());
        Assert.Equal(liveIds, SearchIds(searcher, new TermQuery("body", "shared"), 10).Order().ToArray());
        Assert.Equal(new[] { "doc-03" }, SearchIds(searcher, new TermQuery("id", "doc-03"), 10));
        Assert.Empty(SearchIds(searcher, new TermQuery("id", "doc-01"), 10));
        JsonElement vectorQuery = manifest.GetProperty("VectorQuery");
        string[] vectorHits = SearchIds(searcher, new VectorQuery("embedding",
            vectorQuery.GetProperty("Values").EnumerateArray().Select(value => value.GetSingle()).ToArray(),
            vectorQuery.GetProperty("TopK").GetInt32(), 32), vectorQuery.GetProperty("TopK").GetInt32());
        Assert.Equal(vectorQuery.GetProperty("ExpectedNeighbourIds").EnumerateArray().Select(value => value.GetString()), vectorHits);
        JsonElement geoQuery = manifest.GetProperty("GeoQuery");
        string[] geoHits = SearchIds(searcher, new GeoDistanceQuery(geoQuery.GetProperty("Field").GetString()!,
            geoQuery.GetProperty("Latitude").GetDouble(), geoQuery.GetProperty("Longitude").GetDouble(),
            geoQuery.GetProperty("RadiusMetres").GetDouble()), 10);
        Assert.Equal(geoQuery.GetProperty("ExpectedIds").EnumerateArray().Select(value => value.GetString()), geoHits);
        string[] sortedIds = searcher.Search(new TermQuery("body", "migration"), 10, SortField.String("sort:id"),
                new SearchOptions { CancellationToken = TestContext.Current.CancellationToken }).ScoreDocs
            .Select(hit => Assert.Single(searcher.GetStoredFields(hit.DocId)["id"])).ToArray();
        Assert.Equal(liveIds, sortedIds);
        return new CorpusSnapshot(snapshots.Values.ToArray(), liveSnapshots.Values.ToArray(), liveIds, vectorHits, geoHits, sortedIds);
    }

    private static string[] SearchIds(IndexSearcher searcher, Query query, int count)
        => searcher.Search(query, count, TestContext.Current.CancellationToken).ScoreDocs
            .Select(hit => Assert.Single(searcher.GetStoredFields(hit.DocId)["id"])).ToArray();

    private static byte[][] CaptureHnswBodies(string path)
    {
        using var directory = new MMapDirectory(path);
        return IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentInfos.Select(segment =>
        {
            using ISegmentFileSource source = segment.IsCompoundFile
                ? new CompoundSegmentFileSource(directory, segment.SegmentId)
                : new LooseSegmentFileSource(directory, segment.SegmentId);
            using var input = source.OpenInput(VectorFilePaths.HnswFile(segment.SegmentId, "embedding"));
            using var frame = CodecFileReader.OpenSupported(input, VectorCodecFiles.Hnsw);
            Assert.Equal(1, frame.FormatVersion);
            return frame.ReadBody();
        }).ToArray();
    }

    private static void AssertCurrentFormats(string path, JsonElement manifest)
    {
        using var directory = new MMapDirectory(path);
        // Migration retains original immutable bytes. After commit retention runs,
        // those unreferenced originals can appear in the separate orphan inventory.
        // Prove every file in the current committed index is already current.
        var inventory = IndexFormatInspector.Inspect(directory);
        var currentCommitPlan = IndexCodecMigrator.Plan(inventory with { OrphanFiles = [] });
        Assert.True(currentCommitPlan.CanExecute);
        Assert.Empty(currentCommitPlan.Actions);
        foreach (var segment in IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentInfos)
        {
            using ISegmentFileSource source = segment.IsCompoundFile
                ? new CompoundSegmentFileSource(directory, segment.SegmentId)
                : new LooseSegmentFileSource(directory, segment.SegmentId);
            foreach (var (name, descriptor, version) in new[]
            {
                (segment.SegmentId + ".fdt", StoredFieldsCodecFiles.Data, 5),
                (segment.SegmentId + ".fdx", StoredFieldsCodecFiles.Index, 5),
                (segment.SegmentId + ".dvs", DocValuesCodecFiles.Sorted, 3),
                (VectorFilePaths.VectorFile(segment.SegmentId, "embedding"), VectorCodecFiles.Float32, 2),
                (VectorFilePaths.HnswFile(segment.SegmentId, "embedding"), VectorCodecFiles.Hnsw, 1),
            })
            {
                using var input = source.OpenInput(name);
                using var frame = CodecFileReader.OpenSupported(input, descriptor);
                Assert.Equal(version, frame.FormatVersion);
                byte[] body = frame.ReadBody();
                if (descriptor == VectorCodecFiles.Float32)
                {
                    using var reader = new SegmentReader(directory, segment);
                    var expectedVectors = manifest.GetProperty("Documents").EnumerateArray()
                        .Where(doc => doc.GetProperty("Vector").ValueKind != JsonValueKind.Null)
                        .Select(doc => doc.GetProperty("Id").GetString()!).ToHashSet(StringComparer.Ordinal);
                    byte expected = 0;
                    for (int docId = 0; docId < reader.MaxDoc; docId++)
                        if (expectedVectors.Contains(Assert.Single(reader.GetStoredFields(docId)["id"])))
                            expected |= (byte)(1 << docId);
                    Assert.Equal(expected, body[9]);
                }
            }
        }
    }

    private static string Issues(IndexCodecMigrationResult result)
        => string.Join("; ", result.Issues.Select(issue => issue.Message));

    private static void AssertSnapshotEqual(CorpusSnapshot expected, CorpusSnapshot actual, bool merged = false)
    {
        Assert.Equal(merged ? expected.LiveDocuments : expected.Documents, actual.Documents);
        Assert.Equal(expected.LiveDocuments, actual.LiveDocuments);
        Assert.Equal(expected.TextIds, actual.TextIds);
        Assert.Equal(expected.VectorIds, actual.VectorIds);
        Assert.Equal(expected.GeoIds, actual.GeoIds);
        Assert.Equal(expected.SortedIds, actual.SortedIds);
    }

    private static void AssertLegacyVectorVersionPolicy(string path, bool accepts)
    {
        // Exercise the frozen release's v1-only format contract, without loading
        // another engine package or downloading anything during ordinary tests.
        CodecFileDescriptor current = VectorCodecFiles.Float32;
        var legacyPolicy = new CodecFileDescriptor(current.FormatId, current.FamilyId, current.DisplayName,
            current.FileMatcher, currentFormatVersion: 1,
            supportedVersions: current.SupportedVersions.Where(version => version.Version == 1),
            accessKind: current.AccessKind, currentFraming: current.CurrentFraming, checksumPolicy: current.ChecksumPolicy);
        using var directory = new MMapDirectory(path);
        foreach (var segment in IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentInfos)
        {
            using ISegmentFileSource source = segment.IsCompoundFile
                ? new CompoundSegmentFileSource(directory, segment.SegmentId)
                : new LooseSegmentFileSource(directory, segment.SegmentId);
            using var input = source.OpenInput(VectorFilePaths.VectorFile(segment.SegmentId, "embedding"));
            if (accepts)
            {
                using var frame = CodecFileReader.OpenSupported(input, legacyPolicy);
                Assert.Equal(1, frame.FormatVersion);
                _ = frame.ReadBody();
            }
            else
            {
                var error = Assert.Throws<CodecFileException>(() => CodecFileReader.OpenSupported(input, legacyPolicy));
                Assert.Equal(CodecFileErrorCode.UnsupportedFormatVersion, error.ErrorCode);
            }
        }
    }

    private sealed record CorpusSnapshot(string[] Documents, string[] LiveDocuments, string[] TextIds,
        string[] VectorIds, string[] GeoIds, string[] SortedIds);
}
