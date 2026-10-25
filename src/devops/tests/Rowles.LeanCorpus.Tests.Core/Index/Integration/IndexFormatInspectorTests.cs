using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class IndexFormatInspectorTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexFormatInspectorTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void Inspect_ValidIndex_ReportsCurrentCodecVersions()
    {
        using var directory = CreateIndex("format_valid");

        var inventory = IndexFormatInspector.Inspect(directory);

        Assert.Equal(directory.DirectoryPath, inventory.DirectoryPath);
        Assert.Equal(1, inventory.CommitGeneration);
        Assert.Single(inventory.SegmentIds);
        var segment = Assert.Single(inventory.Segments);
        Assert.Equal(1, segment.DocCount);
        Assert.Empty(segment.MissingFiles);
        Assert.False(inventory.HasUnsupportedFutureFormat);
        Assert.Contains(segment.Files, file =>
            file.Extension == ".dic" &&
            file.FormatId == "leancorpus.term-dictionary.data" &&
            file.FamilyId == "leancorpus.term-dictionary" &&
            file.FrameKind == CodecFileFrameKind.Canonical &&
            file.FrameVersion == CodecFileWriter.CurrentFrameVersion &&
            file.Version == CodecConstants.TermDictionaryVersion &&
            file.CurrentVersion == CodecConstants.TermDictionaryVersion &&
            file.MagicStatus == CodecMagicStatus.Valid &&
            file.ChecksumAlgorithm == CodecFileChecksumAlgorithm.XxHash64 &&
            file.ChecksumStatus == CodecChecksumStatus.NotVerified &&
            file.IsCurrent);
        Assert.Contains(segment.Files, file =>
            file.Extension == ".seg" &&
            file.FrameKind == CodecFileFrameKind.External &&
            file.MagicStatus == CodecMagicStatus.NotApplicable &&
            file.ChecksumStatus == CodecChecksumStatus.NotApplicable);
    }

    [Fact]
    public void Inspect_FutureCodecVersion_ReportsUnsupportedFutureFormat()
    {
        using var directory = CreateIndex("format_future");
        var dictionaryPath = Directory.GetFiles(directory.DirectoryPath, "*.dic").Single();
        WriteCanonicalFormatVersion(dictionaryPath, CodecConstants.TermDictionaryVersion + 1);

        var inventory = IndexFormatInspector.Inspect(directory);

        Assert.True(inventory.HasUnsupportedFutureFormat);
        Assert.Contains(inventory.Issues, issue => issue.Code == IndexCheckIssueCodes.UnsupportedFutureCodecVersion);
        var segment = Assert.Single(inventory.Segments);
        Assert.Contains(segment.Files, file => file.Extension == ".dic" && !file.IsSupported);
    }

    [Fact]
    public void Inspect_FutureCanonicalFrame_ReportsUnsupportedFrameVersion()
    {
        using var directory = CreateIndex("format_future_frame");
        var dictionaryPath = Directory.GetFiles(directory.DirectoryPath, "*.dic").Single();
        using (var stream = File.Open(dictionaryPath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.Position = sizeof(int);
            stream.WriteByte((byte)(CodecFileWriter.CurrentFrameVersion + 1));
        }

        var inventory = IndexFormatInspector.Inspect(directory);

        Assert.True(inventory.HasUnsupportedFutureFormat);
        Assert.Contains(inventory.Issues, issue => issue.Code == IndexCheckIssueCodes.UnsupportedCodecFrameVersion);
        var file = Assert.Single(Assert.Single(inventory.Segments).Files, candidate => candidate.Extension == ".dic");
        Assert.Equal(CodecFileFrameKind.Canonical, file.FrameKind);
        Assert.Equal(CodecFileWriter.CurrentFrameVersion + 1, file.FrameVersion);
        Assert.Equal(CodecFileErrorCode.UnsupportedFrameVersion, file.ErrorCode);
    }

    [Fact]
    public void Inspect_LegacyEnvelope_ReportsExplicitLegacyFrameAndMagicNotApplicable()
    {
        using var directory = CreateIndex("format_legacy");
        var dictionaryPath = Directory.GetFiles(directory.DirectoryPath, "*.dic").Single();
        WriteLegacyEnvelope(dictionaryPath, CodecConstants.TermDictionaryVersion, [0x01]);

        var file = Assert.Single(
            Assert.Single(IndexFormatInspector.Inspect(directory).Segments).Files,
            candidate => candidate.Extension == ".dic");

        Assert.Equal(CodecFileFrameKind.LegacyEnvelope, file.FrameKind);
        Assert.Equal(CodecMagicStatus.NotApplicable, file.MagicStatus);
        Assert.Equal(CodecChecksumStatus.NotApplicable, file.ChecksumStatus);
        Assert.True(file.IsSupported);
        Assert.False(file.IsCurrent);
    }

    [Theory]
    [InlineData(".num", "leancorpus.numeric-structures.numeric-index")]
    [InlineData(".numl", "leancorpus.numeric-structures.int64-numeric-index")]
    public void Inspect_LegacyHeaderlessSidecar_UsesDescriptorDeclaredVersion(
        string extension,
        string formatId)
    {
        using var directory = CreateNumericIndex("format_headerless_sidecar" + extension.Replace(".", "_", StringComparison.Ordinal));
        var sidecarPath = Directory.GetFiles(directory.DirectoryPath, "*" + extension).Single();
        RewriteCanonicalAsHeaderless(sidecarPath, formatId);

        var file = Assert.Single(
            Assert.Single(IndexFormatInspector.Inspect(directory).Segments).Files,
            candidate => candidate.Extension == extension);

        Assert.Equal(CodecFileFrameKind.LegacyHeaderless, file.FrameKind);
        Assert.Equal(CodecMagicStatus.NotApplicable, file.MagicStatus);
        Assert.Equal(1, file.FormatVersion);
        Assert.True(file.IsSupported);
        Assert.False(file.IsCurrent);
    }

    [Fact]
    public void Inspect_UnknownCanonicalFormat_ReportsStructuredUnknownFormat()
    {
        using var directory = CreateIndex("format_unknown");
        var dictionaryPath = Directory.GetFiles(directory.DirectoryPath, "*.dic").Single();
        ReplaceCanonicalFormatIdWithUnknown(dictionaryPath);

        var inventory = IndexFormatInspector.Inspect(directory);

        Assert.True(inventory.HasUnknownFormat);
        Assert.Contains(inventory.Issues, issue => issue.Code == IndexCheckIssueCodes.UnknownCodecFormat);
        var file = Assert.Single(Assert.Single(inventory.Segments).Files, candidate => candidate.Extension == ".dic");
        Assert.False(file.IsKnownFormat);
        Assert.Equal(CodecFileErrorCode.UnknownFormat, file.ErrorCode);
    }

    [Fact]
    public void Inspect_ExplicitCatalogues_AreIsolatedFromTheDefaultCatalogue()
    {
        using var directory = CreateIndex("format_catalogue_isolation");
        var isolatedCatalogue = CreateIsolatedCatalogue();

        var defaultBefore = IndexFormatInspector.Inspect(directory);
        var isolated = IndexFormatInspector.Inspect(directory, new IndexFormatInspectionOptions
        {
            Catalog = isolatedCatalogue,
        });
        var defaultAfter = IndexFormatInspector.Inspect(directory);

        Assert.False(defaultBefore.HasUnknownFormat);
        Assert.True(isolated.HasUnknownFormat);
        Assert.Contains(isolated.Issues, issue => issue.Code == IndexCheckIssueCodes.UnknownCodecFormat);
        Assert.False(defaultAfter.HasUnknownFormat);
    }

    [Fact]
    public void Inspect_DeepChecksum_CorruptCanonicalBodyReportsInvalidChecksum()
    {
        using var directory = CreateIndex("format_checksum");
        var normsPath = Directory.GetFiles(directory.DirectoryPath, "*.nrm").Single();
        CorruptCanonicalBody(normsPath);

        var inventory = IndexFormatInspector.Inspect(directory, new IndexFormatInspectionOptions { IncludeChecksums = true });

        Assert.Contains(inventory.Issues, issue => issue.Code == IndexCheckIssueCodes.CodecChecksumMismatch);
        var file = Assert.Single(Assert.Single(inventory.Segments).Files, candidate => candidate.Extension == ".nrm");
        Assert.Equal(CodecChecksumStatus.Invalid, file.ChecksumStatus);
        Assert.Equal(CodecFileErrorCode.ChecksumMismatch, file.ErrorCode);
    }

    [Fact]
    public void Inspect_Deep_InvokesDescriptorSemanticValidationHandler()
    {
        using var directory = CreateIndex("format_semantic_validation");
        const string familyId = "example.semantic-validation";
        const string formatId = "example.semantic-validation.data";
        var validationHandler = new RequiredByteValidationHandler(expected: 42);
        var descriptor = new CodecFileDescriptor(
            formatId,
            familyId,
            "Semantic validation data",
            CodecFileMatcher.Extension(".custom"),
            currentFormatVersion: 1,
            supportedVersions:
            [
                new CodecVersionDescriptor(
                    1,
                    "current",
                    isWritable: true,
                    migrationBehaviour: CodecMigrationBehaviour.Reframe),
            ],
            accessKind: CodecAccessKind.Streaming,
            currentFraming: CodecFramingPolicy.Canonical,
            checksumPolicy: CodecChecksumPolicy.XxHash64,
            migrationBehaviour: CodecMigrationBehaviour.Reframe,
            temporaryFileMatchers: [CodecFileMatcher.ExtensionWithTrailingSuffix(".custom", ".codec.tmp")],
            validationHandler: validationHandler);
        var catalog = new CodecCatalogBuilder()
            .AddBuiltIns()
            .Add(new CodecFamilyDescriptor(familyId, "Semantic validation", [descriptor]))
            .Build();
        CodecFileWriter.WriteAtomically(
            Path.Combine(directory.DirectoryPath, "orphan.custom"),
            descriptor,
            durable: false,
            body => body.WriteByte(7));

        var inventory = IndexFormatInspector.Inspect(directory, new IndexFormatInspectionOptions
        {
            Catalog = catalog,
            IncludeChecksums = true,
        });

        Assert.Equal(1, validationHandler.InvocationCount);
        Assert.Contains(inventory.Issues, issue => issue.Code == IndexCheckIssueCodes.CodecSemanticValidationFailure);
        var file = Assert.Single(inventory.OrphanFiles, candidate => candidate.Extension == ".custom");
        Assert.Equal(CodecFileErrorCode.SemanticValidationFailure, file.ErrorCode);
        Assert.False(file.IsSupported);

        var validation = IndexValidator.Check(directory, new IndexCheckOptions
        {
            Catalog = catalog,
            Deep = true,
        });
        Assert.Contains(validation.DetailedIssues, issue =>
            issue.Code == IndexCheckIssueCodes.CodecSemanticValidationFailure);
    }

    [Fact]
    public void Inspect_Deep_InvokesFamilyValidationCoordinator()
    {
        using var directory = CreateIndex("format_family_validation");
        string segmentId = IndexFormatInspector.Inspect(directory).SegmentIds.Single();
        const string familyId = "example.family-validation";
        const string formatId = "example.family-validation.data";
        var coordinator = new RequiredByteFamilyValidationCoordinator(formatId, expected: 42);
        var descriptor = new CodecFileDescriptor(
            formatId,
            familyId,
            "Family validation data",
            CodecFileMatcher.Extension(".family-custom"),
            currentFormatVersion: 1,
            supportedVersions:
            [
                new CodecVersionDescriptor(
                    1,
                    "current",
                    isWritable: true,
                    migrationBehaviour: CodecMigrationBehaviour.Reframe),
            ],
            accessKind: CodecAccessKind.Streaming,
            currentFraming: CodecFramingPolicy.Canonical,
            checksumPolicy: CodecChecksumPolicy.XxHash64,
            migrationBehaviour: CodecMigrationBehaviour.Reframe,
            temporaryFileMatchers: [CodecFileMatcher.ExtensionWithTrailingSuffix(".family-custom", ".codec.tmp")]);
        var catalog = new CodecCatalogBuilder()
            .AddBuiltIns()
            .Add(new CodecFamilyDescriptor(familyId, "Family validation", [descriptor], validationCoordinator: coordinator))
            .Build();
        CodecFileWriter.WriteAtomically(
            Path.Combine(directory.DirectoryPath, segmentId + ".family-custom"),
            descriptor,
            durable: false,
            body => body.WriteByte(7));

        var inventory = IndexFormatInspector.Inspect(directory, new IndexFormatInspectionOptions
        {
            Catalog = catalog,
            IncludeChecksums = true,
        });

        Assert.Equal(1, coordinator.InvocationCount);
        Assert.Contains(inventory.Issues, issue =>
            issue.Code == IndexCheckIssueCodes.CodecSemanticValidationFailure && issue.SegmentId == segmentId);
    }

    [Fact]
    public void Inspect_EmptyDirectory_ReportsNoCommit()
    {
        var path = Path.Combine(_fixture.Path, "format_empty");
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);

        var inventory = IndexFormatInspector.Inspect(directory);

        Assert.Null(inventory.CommitGeneration);
        Assert.Empty(inventory.Segments);
        Assert.Contains(inventory.Issues, issue => issue.Code == IndexCheckIssueCodes.NoCommitFile);
    }

    [Fact]
    public void Inspect_OrphanCodecFile_ReportsOrphanInventory()
    {
        using var directory = CreateIndex("format_orphan");
        var dictionaryPath = Directory.GetFiles(directory.DirectoryPath, "*.dic").Single();
        var orphanPath = Path.Combine(directory.DirectoryPath, "orphan.dic");
        File.Copy(dictionaryPath, orphanPath);

        var inventory = IndexFormatInspector.Inspect(directory);

        var orphan = Assert.Single(inventory.OrphanFiles, file => file.FileName == "orphan.dic");
        Assert.Equal(".dic", orphan.Extension);
        Assert.Equal(CodecConstants.TermDictionaryVersion, orphan.Version);
        Assert.Null(orphan.SegmentId);
    }

    [Fact]
    public void IndexFormatInspector_CompoundSegment_ReportsMemberVersions()
    {
        using var directory = CreateIndex("format_compound", useCompoundFile: true);

        var inventory = IndexFormatInspector.Inspect(directory);

        var segment = Assert.Single(inventory.Segments);
        var container = Assert.Single(segment.Files, file => file.Extension == ".cfs");
        Assert.Equal((byte)1, container.Version);
        Assert.True(container.IsCurrent);
        Assert.Contains(segment.Files, file =>
            file.Extension == ".dic" &&
            file.Version == CodecConstants.TermDictionaryVersion &&
            file.CurrentVersion == CodecConstants.TermDictionaryVersion &&
            file.IsCurrent &&
            file.PhysicalLocation == CodecPhysicalLocationKind.CompoundMember &&
            file.CompoundFileName == container.FileName &&
            file.PhysicalFileName == container.FileName);
        Assert.Contains(segment.Files, file =>
            file.Extension == ".pos" &&
            file.Version == CodecConstants.PostingsVersion &&
            file.IsSupported);
        Assert.Contains(segment.Files, file =>
            file.Extension == ".nrm" &&
            file.Version == CodecConstants.NormsVersion &&
            file.CurrentVersion == CodecConstants.NormsVersion &&
            file.IsCurrent);
        Assert.Contains(segment.Files, file =>
            file.Extension == ".fdt" &&
            file.Version == CodecConstants.StoredFieldsVersion &&
            file.IsSupported);
    }

    [Fact]
    public void Inspect_EquivalentLooseAndCompoundSegments_ReportSameLogicalInventory()
    {
        using var looseDirectory = CreateIndex("format_parity_loose", useCompoundFile: false);
        using var compoundDirectory = CreateIndex("format_parity_compound", useCompoundFile: true);

        var loose = Assert.Single(IndexFormatInspector.Inspect(looseDirectory).Segments);
        var compound = Assert.Single(IndexFormatInspector.Inspect(compoundDirectory).Segments);

        var looseFormats = GetLogicalFormats(loose);
        var compoundFormats = GetLogicalFormats(compound);
        Assert.Equal(looseFormats, compoundFormats);
    }

    private MMapDirectory CreateIndex(string name, bool useCompoundFile = false)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { UseCompoundFile = useCompoundFile });
        var document = new LeanDocument();
        document.Add(new TextField("body", "hello world"));
        document.Add(new StringField("id", "1"));
        writer.AddDocument(document);
        writer.Commit();
        return directory;
    }

    private MMapDirectory CreateNumericIndex(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        var document = new LeanDocument();
        document.Add(new TextField("body", "hello world"));
        document.Add(new NumericField("number", 42));
        document.Add(new Int64Field("number64", 42));
        writer.AddDocument(document);
        writer.Commit();
        return directory;
    }

    private static string[] GetLogicalFormats(SegmentFormatInventory segment)
        => segment.Files
            .Where(static file => file.Extension != ".cfs" && file.Extension != ".seg" && file.Extension != ".stats")
            .Select(static file => $"{file.Extension}|{file.CodecName}|{file.Version}|{file.CurrentVersion}|{file.IsSupported}|{file.IsCurrent}")
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

    private static void WriteCanonicalFormatVersion(string path, int version)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = 6;
        stream.Write(BitConverter.GetBytes(version));
    }

    private static void ReplaceCanonicalFormatIdWithUnknown(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = 5;
        int length = stream.ReadByte();
        Assert.True(length >= 3);
        var replacement = System.Text.Encoding.ASCII.GetBytes("x." + new string('x', length - 2));
        stream.Position = CodecFileWriter.FixedHeaderLength;
        stream.Write(replacement);
    }

    private static void CorruptCanonicalBody(string path)
    {
        long bodyStart;
        using (var input = new IndexInput(path))
        using (var session = CodecFileReader.Open(input))
        {
            Assert.True(session.Metadata.BodyLength > 0);
            bodyStart = session.Metadata.BodyStart;
        }

        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = bodyStart;
        int value = stream.ReadByte();
        stream.Position = bodyStart;
        stream.WriteByte((byte)(value ^ 0xff));
    }

    private static void WriteLegacyEnvelope(string path, byte version, byte[] body)
    {
        using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.WriteByte(version);
        ulong length = (ulong)body.Length << 1;
        while (length >= 0x80)
        {
            stream.WriteByte((byte)(length | 0x80));
            length >>= 7;
        }
        stream.WriteByte((byte)length);
        stream.Write(body);
    }

    private static void RewriteCanonicalAsHeaderless(string path, string formatId)
    {
        byte[] body;
        var descriptor = CodecCatalog.Default.GetFile(formatId);
        using (var input = new IndexInput(path))
        using (var frame = CodecFileReader.Open(input, descriptor))
            body = frame.ReadBody();

        File.WriteAllBytes(path, body);
    }

    private static CodecCatalog CreateIsolatedCatalogue()
    {
        const string familyId = "example.catalogue-isolation";
        var file = new CodecFileDescriptor(
            "example.catalogue-isolation.data",
            familyId,
            "Catalogue isolation data",
            CodecFileMatcher.Extension(".custom"),
            currentFormatVersion: null);

        return new CodecCatalogBuilder()
            .Add(new CodecFamilyDescriptor(familyId, "Catalogue isolation", [file]))
            .Build();
    }

    private sealed class RequiredByteValidationHandler(byte expected) : ICodecFileValidationHandler
    {
        public int InvocationCount { get; private set; }

        public void Validate(IndexInput bodyInput)
        {
            InvocationCount++;
            if (bodyInput.Length != 1 || bodyInput.ReadByte() != expected)
                throw new InvalidDataException($"Expected the byte {expected}.");
        }
    }

    private sealed class RequiredByteFamilyValidationCoordinator(string formatId, byte expected)
        : ICodecFamilyValidationCoordinator
    {
        public int InvocationCount { get; private set; }

        public void Validate(IReadOnlyDictionary<string, IndexInput> bodyInputs)
        {
            InvocationCount++;
            var bodyInput = bodyInputs[formatId];
            if (bodyInput.Length != 1 || bodyInput.ReadByte() != expected)
                throw new InvalidDataException($"Expected the byte {expected}.");
        }
    }
}
