using System.Globalization;
using System.Text.Json;
using OrchardCore.Indexing;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.OrchardCore.Search.Indexing;
using Rowles.LeanCorpus.OrchardCore.Search.Storage;

namespace Rowles.LeanCorpus.OrchardCore.Search.Tests;

[Trait("Area", "Orchard")]
public sealed class MappingAndStorageTests
{
    [Fact]
    public void PathResolverIsDeterministicTenantLocalAndRejectsPathTraversal()
    {
        string root = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("server-tests"), "orchard-path-test");
        var first = new LeanCorpusIndexPathResolver(root, "North/Tenant").Resolve("North/Tenant/Search/../../Lucene");
        var repeated = new LeanCorpusIndexPathResolver(root, "North/Tenant").Resolve("North/Tenant/Search/../../Lucene");
        var otherTenant = new LeanCorpusIndexPathResolver(root, "South").Resolve("North/Tenant/Search/../../Lucene");

        Assert.Equal(first.IndexDirectory, repeated.IndexDirectory);
        Assert.NotEqual(first.IndexDirectory, otherTenant.IndexDirectory);
        Assert.StartsWith(Path.GetFullPath(root), first.IndexDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("..", Path.GetRelativePath(root, first.IndexDirectory), StringComparison.Ordinal);
        Assert.NotEqual(first.SchemaPath, otherTenant.SchemaPath);
        Assert.NotEqual(first.StatePath, otherTenant.StatePath);
    }

    [Fact]
    public void MapperStoresReservedIdentityAndMapsFieldTypes()
    {
        var mapper = new LeanCorpusDocumentMapper(Microsoft.Extensions.Logging.Abstractions.NullLogger<LeanCorpusDocumentMapper>.Instance);
        var source = new DocumentIndex("article-1");
        source.Set("Title", "Crème brûlée 東京 🚀", DocumentIndexOptions.Store);
        source.Set("Category", "software", DocumentIndexOptions.Keyword | DocumentIndexOptions.Store);
        source.Set("Featured", true, DocumentIndexOptions.None);
        source.Set("Sequence", 42, DocumentIndexOptions.Store);
        source.Set("Rating", 4.75d, DocumentIndexOptions.None);
        source.Set("PublishedUtc", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), DocumentIndexOptions.None);
        source.Entries.Add(new DocumentIndex.DocumentIndexEntry("Optional", null, DocumentIndex.Types.Text, DocumentIndexOptions.None));
        source.Entries.Add(new DocumentIndex.DocumentIndexEntry("Location", new DocumentIndex.GeoPoint { Latitude = 51.5m, Longitude = -0.1m },
            DocumentIndex.Types.GeoPoint, DocumentIndexOptions.None));
        source.Entries.Add(new DocumentIndex.DocumentIndexEntry("Embedding", new[] { 0.1f, 0.2f, 0.3f },
            DocumentIndex.Types.Vector, DocumentIndexOptions.None) { Dimensions = 3 });

        LeanCorpusMappedBatch result = mapper.Map([source], new LeanCorpusSchemaManifest());
        var mapped = result.Documents.Single();

        Assert.Equal("article-1", Assert.IsType<StringField>(mapped.GetField(LeanCorpusDocumentMapper.DocumentIdField)).Value);
        Assert.Equal("article-1", Assert.IsType<StringField>(mapped.GetField(LeanCorpusDocumentMapper.ContentItemIdField)).Value);
        Assert.Equal("true", Assert.IsType<StringField>(mapped.GetField(
            LeanCorpusDocumentMapper.ContentItemVersionIdField + LeanCorpusDocumentMapper.NullSuffix)).Value);
        Assert.IsType<TextField>(mapped.GetField("Title"));
        Assert.IsType<StringField>(mapped.GetField("Category"));
        Assert.IsType<StringField>(mapped.GetField("Featured"));
        Assert.Equal(42, Assert.IsType<Int64Field>(mapped.GetField("Sequence")).Value);
        Assert.Equal(4.75d, Assert.IsType<NumericField>(mapped.GetField("Rating")).Value);
        long expectedTicks = new DateTimeOffset(2026, 1, 2, 1, 4, 5, TimeSpan.Zero).UtcDateTime.Ticks;
        Assert.Equal(expectedTicks, Assert.IsType<Int64Field>(mapped.GetField("PublishedUtc")).Value);
        Assert.Equal("true", Assert.IsType<StringField>(mapped.GetField("Optional.__lc_null")).Value);
        Assert.IsType<GeoPointField>(mapped.GetField("Location"));
        Assert.Equal(3, Assert.IsType<VectorField>(mapped.GetField("Embedding")).Value.Length);
        Assert.Equal("keyword", result.Manifest.Fields["Category"].Representation);
        Assert.Equal("standard", result.Manifest.Fields["Title"].Analyser);
        Assert.Equal(3, result.Manifest.Fields["Embedding"].Dimensions);

        var contentItem = new ContentItemDocumentIndex("content-item-1", "version-1");
        LeanCorpusMappedBatch contentItemBatch = mapper.Map([contentItem], new LeanCorpusSchemaManifest());
        var mappedContentItem = contentItemBatch.Documents.Single();
        Assert.Equal("content-item-1", Assert.IsType<StringField>(
            mappedContentItem.GetField(LeanCorpusDocumentMapper.ContentItemIdField)).Value);
        Assert.Equal("version-1", Assert.IsType<StringField>(
            mappedContentItem.GetField(LeanCorpusDocumentMapper.ContentItemVersionIdField)).Value);
    }

    [Fact]
    public void MapperRejectsReservedFieldsAndPersistedTypeConflicts()
    {
        var mapper = new LeanCorpusDocumentMapper(Microsoft.Extensions.Logging.Abstractions.NullLogger<LeanCorpusDocumentMapper>.Instance);
        var reserved = new DocumentIndex("reserved");
        reserved.Set("__lc_internal", "must not overwrite", DocumentIndexOptions.None);
        Assert.Throws<InvalidDataException>(() => mapper.Map([reserved], new LeanCorpusSchemaManifest()));

        var number = new DocumentIndex("number");
        number.Set("Price", 2.5d, DocumentIndexOptions.None);
        LeanCorpusSchemaManifest schema = mapper.Map([number], new LeanCorpusSchemaManifest()).Manifest;
        var text = new DocumentIndex("text");
        text.Set("Price", "two pounds", DocumentIndexOptions.None);
        Assert.Throws<InvalidDataException>(() => mapper.Map([text], schema));

        var collision = new DocumentIndex("null-marker-collision");
        collision.Set("Title.__lc_null", "reserved", DocumentIndexOptions.None);
        Assert.Throws<InvalidDataException>(() => mapper.Map([collision], new LeanCorpusSchemaManifest()));
    }

    [Fact]
    public void SchemaAllowsSingleToMultiValueExpansionAndKeepsItMultiValued()
    {
        var mapper = new LeanCorpusDocumentMapper(Microsoft.Extensions.Logging.Abstractions.NullLogger<LeanCorpusDocumentMapper>.Instance);
        var first = new DocumentIndex("first");
        first.Set("Tag", "one", DocumentIndexOptions.None);
        LeanCorpusSchemaManifest schema = mapper.Map([first], new LeanCorpusSchemaManifest()).Manifest;
        Assert.False(schema.Fields["Tag"].MultiValued);

        var second = new DocumentIndex("second");
        second.Entries.Add(new DocumentIndex.DocumentIndexEntry("Tag", "one", DocumentIndex.Types.Text, DocumentIndexOptions.None));
        second.Entries.Add(new DocumentIndex.DocumentIndexEntry("Tag", "two", DocumentIndex.Types.Text, DocumentIndexOptions.None));
        schema = mapper.Map([second], schema).Manifest;
        Assert.True(schema.Fields["Tag"].MultiValued);

        var third = new DocumentIndex("third");
        third.Set("Tag", "three", DocumentIndexOptions.None);
        Assert.True(mapper.Map([third], schema).Manifest.Fields["Tag"].MultiValued);
    }

    [Fact]
    public void MapperRecordsComplexFieldsAsUnsupportedAndRejectsInvalidGeoAndVectorValues()
    {
        var mapper = new LeanCorpusDocumentMapper(Microsoft.Extensions.Logging.Abstractions.NullLogger<LeanCorpusDocumentMapper>.Instance);
        var complex = new DocumentIndex("complex");
        complex.Entries.Add(new DocumentIndex.DocumentIndexEntry("Metadata", new object(),
            DocumentIndex.Types.Complex, DocumentIndexOptions.Store));
        LeanCorpusMappedBatch complexBatch = mapper.Map([complex], new LeanCorpusSchemaManifest());
        Assert.False(complexBatch.Manifest.Fields["Metadata"].Indexed);
        Assert.Equal("unsupported", complexBatch.Manifest.Fields["Metadata"].Representation);
        Assert.Null(complexBatch.Documents.Single().GetField("Metadata"));

        var invalidGeo = new DocumentIndex("invalid-geo");
        invalidGeo.Entries.Add(new DocumentIndex.DocumentIndexEntry("Location",
            new DocumentIndex.GeoPoint { Latitude = 91m, Longitude = -0.1m },
            DocumentIndex.Types.GeoPoint, DocumentIndexOptions.None));
        Assert.Throws<InvalidDataException>(() => mapper.Map([invalidGeo], new LeanCorpusSchemaManifest()));

        var invalidVector = new DocumentIndex("invalid-vector");
        invalidVector.Entries.Add(new DocumentIndex.DocumentIndexEntry("Embedding", new[] { float.NaN, 0.2f },
            DocumentIndex.Types.Vector, DocumentIndexOptions.None) { Dimensions = 2 });
        Assert.Throws<InvalidDataException>(() => mapper.Map([invalidVector], new LeanCorpusSchemaManifest()));
    }

    [Fact]
    public async Task CursorStateDefaultsToZeroAndPreservesOldValueAcrossUnpublishedFailures()
    {
        string path = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("server-tests"), "orchard-cursor-test", Guid.NewGuid().ToString("N"), "state.json");
        var failures = new SwitchableFailureInjector();
        try
        {
            Assert.Equal(0, await LeanCorpusIndexingStateStore.ReadLastTaskIdAsync(path, TestContext.Current.CancellationToken));
            await LeanCorpusIndexingStateStore.PublishAsync(path, 8, failures, TestContext.Current.CancellationToken);
            using (JsonDocument saved = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)))
            {
                Assert.Equal(1, saved.RootElement.GetProperty("version").GetInt32());
                Assert.Equal(8, saved.RootElement.GetProperty("lastTaskId").GetInt64());
            }

            failures.Point = LeanCorpusFailurePoint.DuringCursorWrite;
            await Assert.ThrowsAsync<IOException>(() => LeanCorpusIndexingStateStore.PublishAsync(path, 9, failures, TestContext.Current.CancellationToken));
            Assert.Equal(8, await LeanCorpusIndexingStateStore.ReadLastTaskIdAsync(path, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "state.json.tmp-*"));

            failures.Point = LeanCorpusFailurePoint.AfterCursorFlushBeforePublish;
            await Assert.ThrowsAsync<IOException>(() => LeanCorpusIndexingStateStore.PublishAsync(path, 10, failures, TestContext.Current.CancellationToken));
            Assert.Equal(8, await LeanCorpusIndexingStateStore.ReadLastTaskIdAsync(path, TestContext.Current.CancellationToken));

            failures.Point = LeanCorpusFailurePoint.AfterCursorPublish;
            await Assert.ThrowsAsync<IOException>(() => LeanCorpusIndexingStateStore.PublishAsync(path, 11, failures, TestContext.Current.CancellationToken));
            Assert.Equal(11, await LeanCorpusIndexingStateStore.ReadLastTaskIdAsync(path, TestContext.Current.CancellationToken));

            await File.WriteAllTextAsync(path, "{", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                LeanCorpusIndexingStateStore.ReadLastTaskIdAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
