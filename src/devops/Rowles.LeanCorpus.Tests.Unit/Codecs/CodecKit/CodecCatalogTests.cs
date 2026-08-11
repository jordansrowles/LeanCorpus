using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class CodecCatalogTests
{
    [Fact(DisplayName = "Catalogue snapshots are immutable after build")]
    public void Build_CreatesImmutableSnapshot()
    {
        var versions = new[] { Version(1, writable: true) };
        var files = new[]
        {
            File("example.format.data", "example.format", ".one", 1, versions),
        };
        var family = new CodecFamilyDescriptor("example.format", "Example", files);
        var builder = new CodecCatalogBuilder().Add(family);
        var catalog = builder.Build();

        versions[0] = Version(2, writable: true);
        files[0] = File("example.format.changed", "example.format", ".two", 1, [Version(1, writable: true)]);
        builder.Add(new CodecFamilyDescriptor(
            "example.later",
            "Later",
            [File("example.later.data", "example.later", ".later", 1, [Version(1, writable: true)])]));

        var registered = catalog.GetFile("example.format.data");
        Assert.Single(catalog.Families);
        Assert.Single(catalog.Files);
        Assert.Equal(1, registered.SupportedVersions[0].Version);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<CodecFamilyDescriptor>)catalog.Families).Add(family));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<CodecVersionDescriptor>)registered.SupportedVersions).Add(Version(2)));
    }

    [Fact(DisplayName = "Build rejects duplicate family IDs")]
    public void Build_DuplicateFamilyId_Throws()
    {
        var builder = new CodecCatalogBuilder()
            .Add(Family("example.family", "example.family.one", ".one"))
            .Add(Family("example.family", "example.family.two", ".two"));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("Duplicate codec family ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Build rejects duplicate format IDs")]
    public void Build_DuplicateFormatId_Throws()
    {
        var builder = new CodecCatalogBuilder()
            .Add(Family("example.one", "example.shared", ".one"))
            .Add(Family("example.two", "example.shared", ".two"));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("Duplicate codec format ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Build rejects duplicate physical file claims")]
    public void Build_DuplicatePhysicalClaim_Throws()
    {
        var builder = new CodecCatalogBuilder()
            .Add(Family("example.one", "example.one.data", ".same"))
            .Add(Family("example.two", "example.two.data", ".SAME"));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("claim the same physical file role", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Build rejects overlapping physical file claims")]
    public void Build_OverlappingPhysicalClaim_Throws()
    {
        var extensionFamily = Family("example.one", "example.one.data", ".data");
        var exactFile = new CodecFileDescriptor(
            "example.two.data",
            "example.two",
            "Exact data",
            CodecFileMatcher.Exact("fixed.data"),
            1,
            [Version(1, writable: true)],
            CodecAccessKind.Materialised,
            CodecFramingPolicy.Canonical,
            CodecChecksumPolicy.XxHash64,
            CodecMigrationBehaviour.Reframe,
            [CodecFileMatcher.Extension(".two.tmp")]);
        var exactFamily = new CodecFamilyDescriptor("example.two", "Exact", [exactFile]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CodecCatalogBuilder().Add(extensionFamily).Add(exactFamily).Build());

        Assert.Contains("overlapping physical file claims", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Build rejects versions that are not strictly increasing")]
    public void Build_UnorderedVersions_Throws()
    {
        var file = File(
            "example.format.data",
            "example.format",
            ".data",
            2,
            [Version(2, writable: true), Version(1)]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CodecCatalogBuilder()
                .Add(new CodecFamilyDescriptor("example.format", "Example", [file]))
                .Build());

        Assert.Contains("strictly increasing", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Build rejects a current version absent from supported versions")]
    public void Build_CurrentVersionNotRegistered_Throws()
    {
        var file = File(
            "example.format.data",
            "example.format",
            ".data",
            2,
            [Version(1, writable: true)]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CodecCatalogBuilder()
                .Add(new CodecFamilyDescriptor("example.format", "Example", [file]))
                .Build());

        Assert.Contains("is not registered", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Build requires the current version to be writable")]
    public void Build_CurrentVersionNotWritable_Throws()
    {
        var file = File(
            "example.format.data",
            "example.format",
            ".data",
            2,
            [Version(1, writable: true), Version(2)]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CodecCatalogBuilder()
                .Add(new CodecFamilyDescriptor("example.format", "Example", [file]))
                .Build());

        Assert.Contains("current version must be readable and writable", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Build requires current version to be newest writable version")]
    public void Build_CurrentVersionNotNewestWritable_Throws()
    {
        var file = File(
            "example.format.data",
            "example.format",
            ".data",
            1,
            [Version(1, writable: true), Version(2, writable: true)]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CodecCatalogBuilder()
                .Add(new CodecFamilyDescriptor("example.format", "Example", [file]))
                .Build());

        Assert.Contains("newest writable version", exception.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "Build rejects invalid identifiers")]
    [InlineData("unnamespaced")]
    [InlineData("Example.format")]
    [InlineData("example..format")]
    [InlineData("example.format_name")]
    [InlineData("example.-format")]
    public void Build_InvalidIdentifier_Throws(string identifier)
    {
        var family = new CodecFamilyDescriptor(
            identifier,
            "Example",
            [File("example.format.data", identifier, ".data", 1, [Version(1, writable: true)])]);

        Assert.Throws<ArgumentException>(() => new CodecCatalogBuilder().Add(family).Build());
    }

    [Fact(DisplayName = "Build accepts a 64-byte format identifier")]
    public void Build_SixtyFourByteIdentifier_Succeeds()
    {
        var formatId = "a." + new string('b', 62);
        var family = new CodecFamilyDescriptor(
            "example.family",
            "Example",
            [File(formatId, "example.family", ".data", 1, [Version(1, writable: true)])]);

        var catalog = new CodecCatalogBuilder().Add(family).Build();

        Assert.Equal(64, formatId.Length);
        Assert.Same(catalog.Files[0], catalog.GetFile(formatId));
    }

    [Fact(DisplayName = "Build rejects a 65-byte format identifier")]
    public void Build_SixtyFiveByteIdentifier_Throws()
    {
        var formatId = "a." + new string('b', 63);
        var family = new CodecFamilyDescriptor(
            "example.family",
            "Example",
            [File(formatId, "example.family", ".data", 1, [Version(1, writable: true)])]);

        var exception = Assert.Throws<ArgumentException>(() =>
            new CodecCatalogBuilder().Add(family).Build());

        Assert.Equal(65, formatId.Length);
        Assert.Contains("at most 64 ASCII bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Default catalogue represents every known persistent format")]
    public void Default_ContainsEveryKnownBuiltIn()
    {
        var expected = new (string FileName, int? Version)[]
        {
            ("seg_1.dic", CodecConstants.TermDictionaryVersion),
            ("seg_1.pos", CodecConstants.PostingsVersion),
            ("seg_1.nrm", CodecConstants.NormsVersion),
            ("seg_1.fln", CodecConstants.FieldLengthVersion),
            ("seg_1.fdt", CodecConstants.StoredFieldsVersion),
            ("seg_1.fdx", CodecConstants.StoredFieldsVersion),
            ("seg_1.tvd", CodecConstants.TermVectorsVersion),
            ("seg_1.tvx", CodecConstants.TermVectorsVersion),
            ("seg_1.dvn", CodecConstants.NumericDocValuesVersion),
            ("seg_1.dvs", CodecConstants.SortedDocValuesVersion),
            ("seg_1.dss", CodecConstants.SortedSetDocValuesVersion),
            ("seg_1.dsn", CodecConstants.SortedNumericDocValuesVersion),
            ("seg_1.dvb", CodecConstants.BinaryDocValuesVersion),
            ("seg_1.dvnl", CodecConstants.Int64DocValuesVersion),
            ("seg_1.dsnl", CodecConstants.Int64SortedNumericDocValuesVersion),
            ("seg_1.bkd", CodecConstants.BKDVersion),
            ("seg_1.bkdl", CodecConstants.Int64BKDVersion),
            ("seg_1.num", 1),
            ("seg_1.numl", 1),
            ("seg_1_v_title.vec", CodecConstants.VectorVersion),
            ("seg_1_v_title.vq", CodecConstants.QuantisedVectorVersion),
            ("seg_1_v_title.hnsw", CodecConstants.HnswVersion),
            ("seg_1_gen_2.del", CodecConstants.RoaringBitmapVersion),
            ("seg_1.pbs", 1),
            ("seg_1.seg", null),
            ("seg_1.stats.json", null),
            ("seg_1.cfs", null),
            ("segments_42", null),
            ("stats_42.json", null),
            ("migration_state.json", null),
        };

        foreach (var (fileName, version) in expected)
        {
            Assert.True(CodecCatalog.Default.TryMatchFile(fileName, out var descriptor), fileName);
            Assert.NotNull(descriptor);
            Assert.Equal(version, descriptor.CurrentFormatVersion);
        }

        Assert.Equal(expected.Length, CodecCatalog.Default.Files.Count);
    }

    [Fact(DisplayName = "Every current built-in version is registered and newest writable")]
    public void Default_CurrentVersionsAreRegisteredAndNewestWritable()
    {
        foreach (var file in CodecCatalog.Default.Files.Where(static file => file.CurrentFormatVersion.HasValue))
        {
            var current = Assert.Single(
                file.SupportedVersions,
                version => version.Version == file.CurrentFormatVersion);

            Assert.True(current.IsReadable, file.FormatId);
            Assert.True(current.IsWritable, file.FormatId);
            Assert.Equal(CodecFramingPolicy.Canonical, file.CurrentFraming);
            Assert.Equal(CodecChecksumPolicy.XxHash64, file.ChecksumPolicy);
            Assert.NotEqual(CodecAccessKind.External, file.AccessKind);
            Assert.NotEqual(CodecMigrationBehaviour.None, file.MigrationBehaviour);
            Assert.Equal(file.MigrationBehaviour, current.MigrationBehaviour);
            Assert.NotEmpty(file.TemporaryFileMatchers);
            Assert.DoesNotContain(
                file.SupportedVersions,
                version => version.Version > current.Version && version.IsWritable);
        }
    }

    [Fact(DisplayName = "Every built-in declares complete storage policy metadata")]
    public void Default_StoragePoliciesAreComplete()
    {
        foreach (var file in CodecCatalog.Default.Files)
        {
            Assert.True(Enum.IsDefined(file.AccessKind), file.FormatId);
            Assert.True(Enum.IsDefined(file.CurrentFraming), file.FormatId);
            Assert.True(Enum.IsDefined(file.ChecksumPolicy), file.FormatId);
            Assert.True(Enum.IsDefined(file.MigrationBehaviour), file.FormatId);
            Assert.NotEmpty(file.TemporaryFileMatchers);

            if (!file.CurrentFormatVersion.HasValue)
            {
                Assert.NotEqual(CodecFramingPolicy.Canonical, file.CurrentFraming);
                Assert.Equal(CodecChecksumPolicy.None, file.ChecksumPolicy);
                continue;
            }

            foreach (var version in file.SupportedVersions)
            {
                Assert.NotEqual(CodecLegacyFraming.None, version.LegacyFraming);
                Assert.NotEqual(CodecMigrationBehaviour.None, version.MigrationBehaviour);
            }
        }
    }

    [Theory(DisplayName = "Headerless legacy sidecars declare their framing explicitly")]
    [InlineData("leancorpus.numeric-structures.numeric-index")]
    [InlineData("leancorpus.numeric-structures.int64-numeric-index")]
    [InlineData("leancorpus.deletes.parent-bitset")]
    public void Default_HeaderlessLegacySidecars_DeclareHeaderlessFraming(string formatId)
    {
        var descriptor = CodecCatalog.Default.GetFile(formatId);

        var version = Assert.Single(descriptor.SupportedVersions);
        Assert.True((version.LegacyFraming & CodecLegacyFraming.Headerless) != 0);
        Assert.False((version.LegacyFraming & CodecLegacyFraming.CustomHeader) != 0);
    }

    [Fact(DisplayName = "File matchers handle paths and numbered names precisely")]
    public void FileMatchers_MatchLogicalNames()
    {
        Assert.True(CodecFileMatcher.Extension("vec").IsMatch(@"C:\index\seg_v_title.vec"));
        Assert.True(CodecFileMatcher.Numbered("segments_").IsMatch("/index/segments_12"));
        Assert.True(CodecFileMatcher.Numbered("stats_", ".json").IsMatch("stats_12.json"));
        Assert.False(CodecFileMatcher.Numbered("segments_").IsMatch("segments_latest"));
        Assert.False(CodecFileMatcher.Numbered("stats_", ".json").IsMatch("stats_.json"));
        Assert.True(CodecFileMatcher.ExtensionWithTrailingSuffix(".dic", ".tmp").IsMatch("seg_1.dic.tmp"));
        Assert.True(CodecFileMatcher.ExtensionWithTrailingSuffix(".dic", ".tmp").IsMatch("seg_1.dic.body.tmp"));
        Assert.True(CodecFileMatcher.ExtensionWithTrailingSuffix(".dic", ".tmp").IsMatch("seg_1.dic.0123456789abcdef.tmp"));
        Assert.False(CodecFileMatcher.ExtensionWithTrailingSuffix(".dic", ".tmp").IsMatch("seg_1.pos.tmp"));
    }

    [Theory(DisplayName = "Default catalogue resolves recognised temporary files")]
    [InlineData("seg_1.dic.tmp", "leancorpus.term-dictionary.data")]
    [InlineData("seg_1.dic.body.tmp", "leancorpus.term-dictionary.data")]
    [InlineData("seg_1.dic.0123456789abcdef.tmp", "leancorpus.term-dictionary.data")]
    [InlineData("segments_42.tmp", "leancorpus.segment-store.commit")]
    [InlineData("migration_state.json.tmp", "leancorpus.segment-store.migration-state")]
    public void Default_TemporaryFilesResolveToOwner(string fileName, string expectedFormatId)
    {
        Assert.True(CodecCatalog.Default.TryMatchTemporaryFile(fileName, out var descriptor));
        Assert.NotNull(descriptor);
        Assert.Equal(expectedFormatId, descriptor.FormatId);
    }

    private static CodecFamilyDescriptor Family(string familyId, string formatId, string extension)
        => new(
            familyId,
            "Example",
            [File(formatId, familyId, extension, 1, [Version(1, writable: true)])]);

    private static CodecFileDescriptor File(
        string formatId,
        string familyId,
        string extension,
        int currentVersion,
        IEnumerable<CodecVersionDescriptor> versions)
        => new(
            formatId,
            familyId,
            "Example data",
            CodecFileMatcher.Extension(extension),
            currentVersion,
            versions,
            CodecAccessKind.Materialised,
            CodecFramingPolicy.Canonical,
            CodecChecksumPolicy.XxHash64,
            CodecMigrationBehaviour.Reframe,
            [CodecFileMatcher.Extension(extension + ".tmp")]);

    private static CodecVersionDescriptor Version(int version, bool writable = false)
        => new(
            version,
            $"v{version}",
            isReadable: true,
            isWritable: writable,
            CodecLegacyFraming.CodecKitEnvelope,
            CodecMigrationBehaviour.Reframe);
}
