namespace Rowles.LeanCorpus.Tests.Core.Index.Migration;

/// <summary>
/// Unit tests for <see cref="IndexCodecMigrator"/> plan building, driven through the
/// internal <c>Plan(IndexFormatInventory, CodecCatalog)</c> overload with in-memory
/// inventory records and a custom codec catalogue. No filesystem is used.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
[Area(TestArea.CodecKit)]
public sealed class IndexCodecMigratorPlanTests
{
    [Fact(DisplayName = "Plan: Reframe behaviour produces a Reframe action")]
    public void Plan_ReframeBehaviour_ProducesReframeAction()
    {
        var descriptor = CreateDescriptor(
            "unit-test.plan.reframe", "unit-test.plan.family", ".rfr",
            CodecMigrationBehaviour.Reframe, CodecMigrationBehaviour.Reframe);
        var catalog = BuildCatalog("unit-test.plan.family", "plan family", descriptor, coordinator: null);
        var inventory = BuildInventory([
            CreateFile("unit-test.plan.reframe", "unit-test.plan.family", "seg1.rfr", "seg1", 1, 2),
        ]);

        var plan = IndexCodecMigrator.Plan(inventory, catalog);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(IndexCodecMigrationActionKind.Reframe, action.Kind);
        Assert.True(action.CanExecute);
        Assert.Equal((byte)1, action.FromVersion);
        Assert.Equal((byte)2, action.ToVersion);
        Assert.Null(action.ReasonCannotExecute);
    }

    [Fact(DisplayName = "Plan: Rewrite with a migration handler produces an executable Rewrite action")]
    public void Plan_RewriteWithHandler_ProducesExecutableRewrite()
    {
        var descriptor = CreateDescriptor(
            "unit-test.plan.rewrite-handled", "unit-test.plan.family", ".rh",
            CodecMigrationBehaviour.Rewrite, CodecMigrationBehaviour.Rewrite,
            migrationHandler: new NoOpMigrationHandler());
        var catalog = BuildCatalog("unit-test.plan.family", "plan family", descriptor, coordinator: null);
        var inventory = BuildInventory([
            CreateFile("unit-test.plan.rewrite-handled", "unit-test.plan.family", "seg1.rh", "seg1", 1, 2),
        ]);

        var plan = IndexCodecMigrator.Plan(inventory, catalog);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(IndexCodecMigrationActionKind.Rewrite, action.Kind);
        Assert.True(action.CanExecute);
    }

    [Fact(DisplayName = "Plan: Rewrite without a writer produces an Unsupported action")]
    public void Plan_RewriteWithoutHandler_ProducesUnsupported()
    {
        var descriptor = CreateDescriptor(
            "unit-test.plan.rewrite-bare", "unit-test.plan.family", ".rb",
            CodecMigrationBehaviour.Rewrite, CodecMigrationBehaviour.Rewrite);
        var catalog = BuildCatalog("unit-test.plan.family", "plan family", descriptor, coordinator: null);
        var inventory = BuildInventory([
            CreateFile("unit-test.plan.rewrite-bare", "unit-test.plan.family", "seg1.rb", "seg1", 1, 2),
        ]);

        var plan = IndexCodecMigrator.Plan(inventory, catalog);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(IndexCodecMigrationActionKind.Unsupported, action.Kind);
        Assert.False(action.CanExecute);
        Assert.NotNull(action.ReasonCannotExecute);
        Assert.False(plan.CanExecute);
    }

    [Fact(DisplayName = "Plan: Coordinated family with a coordinator dedupes into one action")]
    public void Plan_CoordinatedRewrite_WithCoordinator_DedupesFamilyFiles()
    {
        var first = CreateDescriptor(
            "unit-test.coord.a", "unit-test.coord.family", ".ca",
            CodecMigrationBehaviour.CoordinatedRewrite, CodecMigrationBehaviour.CoordinatedRewrite);
        var second = CreateDescriptor(
            "unit-test.coord.b", "unit-test.coord.family", ".cb",
            CodecMigrationBehaviour.CoordinatedRewrite, CodecMigrationBehaviour.CoordinatedRewrite);
        var catalog = BuildCatalog(
            "unit-test.coord.family", "coord family", [first, second],
            coordinator: new NoOpFamilyCoordinator());
        var inventory = BuildInventory([
            CreateFile("unit-test.coord.a", "unit-test.coord.family", "seg1.ca", "seg1", 1, 2),
            CreateFile("unit-test.coord.b", "unit-test.coord.family", "seg1.cb", "seg1", 1, 2),
        ]);

        var plan = IndexCodecMigrator.Plan(inventory, catalog);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(IndexCodecMigrationActionKind.CoordinatedRewrite, action.Kind);
        Assert.Equal(2, action.SourcePaths.Count);
        Assert.True(action.CanExecute);
    }

    [Fact(DisplayName = "Plan: Coordinated family without a coordinator produces an Unsupported action")]
    public void Plan_CoordinatedRewrite_WithoutCoordinator_Unsupported()
    {
        var first = CreateDescriptor(
            "unit-test.coord.a", "unit-test.coord.family", ".ca",
            CodecMigrationBehaviour.CoordinatedRewrite, CodecMigrationBehaviour.CoordinatedRewrite);
        var second = CreateDescriptor(
            "unit-test.coord.b", "unit-test.coord.family", ".cb",
            CodecMigrationBehaviour.CoordinatedRewrite, CodecMigrationBehaviour.CoordinatedRewrite);
        var catalog = BuildCatalog(
            "unit-test.coord.family", "coord family", [first, second],
            coordinator: null);
        var inventory = BuildInventory([
            CreateFile("unit-test.coord.a", "unit-test.coord.family", "seg1.ca", "seg1", 1, 2),
            CreateFile("unit-test.coord.b", "unit-test.coord.family", "seg1.cb", "seg1", 1, 2),
        ]);

        var plan = IndexCodecMigrator.Plan(inventory, catalog);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(IndexCodecMigrationActionKind.Unsupported, action.Kind);
        Assert.False(action.CanExecute);
    }

    [Fact(DisplayName = "Plan: Version-specific behaviour overrides the descriptor default")]
    public void Plan_VersionSpecificBehaviour_OverridesDescriptorDefault()
    {
        var descriptor = CreateDescriptor(
            "unit-test.plan.versioned", "unit-test.plan.family", ".ver",
            CodecMigrationBehaviour.Rewrite, CodecMigrationBehaviour.Reframe,
            migrationHandler: new NoOpMigrationHandler());
        var catalog = BuildCatalog("unit-test.plan.family", "plan family", descriptor, coordinator: null);

        var planV1 = IndexCodecMigrator.Plan(
            BuildInventory([CreateFile("unit-test.plan.versioned", "unit-test.plan.family", "seg1.ver", "seg1", 1, 2)]),
            catalog);
        Assert.Equal(IndexCodecMigrationActionKind.Rewrite, Assert.Single(planV1.Actions).Kind);

        var planUnknown = IndexCodecMigrator.Plan(
            BuildInventory([CreateFile("unit-test.plan.versioned", "unit-test.plan.family", "seg1.ver", "seg1", 99, 2)]),
            catalog);
        Assert.Equal(IndexCodecMigrationActionKind.Reframe, Assert.Single(planUnknown.Actions).Kind);
    }

    [Fact(DisplayName = "Plan: Current-version file produces no action")]
    public void Plan_CurrentFile_NoAction()
    {
        var descriptor = CreateDescriptor(
            "unit-test.plan.reframe", "unit-test.plan.family", ".rfr",
            CodecMigrationBehaviour.Reframe, CodecMigrationBehaviour.Reframe);
        var catalog = BuildCatalog("unit-test.plan.family", "plan family", descriptor, coordinator: null);
        var inventory = BuildInventory([
            CreateFile("unit-test.plan.reframe", "unit-test.plan.family", "seg1.rfr", "seg1", 2, 2, isCurrent: true),
        ]);

        var plan = IndexCodecMigrator.Plan(inventory, catalog);

        Assert.Empty(plan.Actions);
    }

    [Fact(DisplayName = "Plan: Orphan files produce actions")]
    public void Plan_OrphanFiles_ProduceActions()
    {
        var descriptor = CreateDescriptor(
            "unit-test.plan.reframe", "unit-test.plan.family", ".rfr",
            CodecMigrationBehaviour.Reframe, CodecMigrationBehaviour.Reframe);
        var catalog = BuildCatalog("unit-test.plan.family", "plan family", descriptor, coordinator: null);
        var inventory = BuildInventory(
            segmentFiles: [],
            orphanFiles: [CreateFile("unit-test.plan.reframe", "unit-test.plan.family", "orphan.rfr", "seg1", 1, 2)]);

        var plan = IndexCodecMigrator.Plan(inventory, catalog);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(IndexCodecMigrationActionKind.Reframe, action.Kind);
    }

    [Fact(DisplayName = "Plan: CanExecute is false when any action is unsupported")]
    public void Plan_CanExecute_FalseWhenAnyUnsupported()
    {
        var reframeDescriptor = CreateDescriptor(
            "unit-test.plan.reframe", "unit-test.plan.family-a", ".rfr",
            CodecMigrationBehaviour.Reframe, CodecMigrationBehaviour.Reframe);
        var rewriteDescriptor = CreateDescriptor(
            "unit-test.plan.rewrite-bare", "unit-test.plan.family-b", ".rb",
            CodecMigrationBehaviour.Rewrite, CodecMigrationBehaviour.Rewrite);
        var catalog = new CodecCatalogBuilder()
            .AddBuiltIns()
            .Add(new CodecFamilyDescriptor("unit-test.plan.family-a", "family a", [reframeDescriptor]))
            .Add(new CodecFamilyDescriptor("unit-test.plan.family-b", "family b", [rewriteDescriptor]))
            .Build();
        var inventory = BuildInventory([
            CreateFile("unit-test.plan.reframe", "unit-test.plan.family-a", "seg1.rfr", "seg1", 1, 2),
            CreateFile("unit-test.plan.rewrite-bare", "unit-test.plan.family-b", "seg1.rb", "seg1", 1, 2),
        ]);

        var plan = IndexCodecMigrator.Plan(inventory, catalog);

        Assert.Equal(2, plan.Actions.Count);
        Assert.False(plan.CanExecute);
        Assert.Single(plan.Actions, static action => !action.CanExecute);
    }

    private static CodecFileDescriptor CreateDescriptor(
        string formatId, string familyId, string extension,
        CodecMigrationBehaviour legacyBehaviour,
        CodecMigrationBehaviour currentBehaviour,
        ICodecFileMigrationHandler? migrationHandler = null,
        int legacyVersion = 1, int currentVersion = 2)
        => new(
            formatId, familyId, formatId,
            CodecFileMatcher.Extension(extension),
            currentFormatVersion: currentVersion,
            supportedVersions:
            [
                new CodecVersionDescriptor(legacyVersion, "legacy",
                    legacyFraming: CodecLegacyFraming.CodecKitEnvelope,
                    migrationBehaviour: legacyBehaviour),
                new CodecVersionDescriptor(currentVersion, "current",
                    isWritable: true,
                    migrationBehaviour: currentBehaviour),
            ],
            accessKind: CodecAccessKind.Streaming,
            currentFraming: CodecFramingPolicy.Canonical,
            checksumPolicy: CodecChecksumPolicy.XxHash64,
            migrationBehaviour: currentBehaviour,
            temporaryFileMatchers: [CodecFileMatcher.ExtensionWithTrailingSuffix(extension, ".codec.tmp")],
            migrationHandler: migrationHandler);

    private static CodecCatalog BuildCatalog(
        string familyId, string displayName, CodecFileDescriptor descriptor,
        ICodecFamilyMigrationCoordinator? coordinator)
        => new CodecCatalogBuilder()
            .AddBuiltIns()
            .Add(new CodecFamilyDescriptor(familyId, displayName, [descriptor], migrationCoordinator: coordinator))
            .Build();

    private static CodecCatalog BuildCatalog(
        string familyId, string displayName, IReadOnlyList<CodecFileDescriptor> descriptors,
        ICodecFamilyMigrationCoordinator? coordinator)
        => new CodecCatalogBuilder()
            .AddBuiltIns()
            .Add(new CodecFamilyDescriptor(familyId, displayName, descriptors, migrationCoordinator: coordinator))
            .Build();

    private static CodecFileInventory CreateFile(
        string formatId, string familyId, string fileName, string segmentId,
        int version, int currentVersion, bool isCurrent = false)
        => new()
        {
            FileName = fileName,
            Extension = Path.GetExtension(fileName),
            CodecName = formatId,
            FormatId = formatId,
            FamilyId = familyId,
            FormatVersion = version,
            CurrentFormatVersion = currentVersion,
            IsSupported = true,
            IsCurrent = isCurrent,
            MagicStatus = CodecMagicStatus.Valid,
            SegmentId = segmentId,
        };

    private static IndexFormatInventory BuildInventory(
        IReadOnlyList<CodecFileInventory> segmentFiles,
        IReadOnlyList<CodecFileInventory>? orphanFiles = null)
        => new()
        {
            DirectoryPath = "/virtual",
            CommitGeneration = 1,
            ContentToken = 0,
            SegmentIds = segmentFiles.Select(static f => f.SegmentId!).Distinct().ToArray(),
            Segments = segmentFiles.Count == 0
                ? Array.Empty<SegmentFormatInventory>()
                : [new SegmentFormatInventory
                {
                    SegmentId = segmentFiles[0].SegmentId!,
                    Files = segmentFiles,
                    MissingFiles = [],
                    Warnings = [],
                }],
            OrphanFiles = orphanFiles ?? [],
            Issues = [],
            HasUnsupportedFutureFormat = false,
        };

    private sealed class NoOpMigrationHandler : ICodecFileMigrationHandler
    {
        public void Migrate(IndexInput sourceBody, IndexOutput targetBody)
        {
        }
    }

    private sealed class NoOpFamilyCoordinator : ICodecFamilyMigrationCoordinator
    {
        public void Migrate(
            IReadOnlyDictionary<string, IndexInput> sourceBodies,
            IReadOnlyDictionary<string, IndexOutput> targetBodies)
        {
        }
    }
}
