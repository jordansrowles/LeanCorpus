using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Codecs.CodecKit;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class CodecLifecycleTests
{
    [Fact]
    public void Build_FullyRetiredFormat_RemainsRegisteredForReading()
    {
        var descriptor = RetiredDescriptor();
        var catalogue = new CodecCatalogBuilder()
            .Add(new CodecFamilyDescriptor("example.retired", "Retired", [descriptor]))
            .Build();

        Assert.Same(descriptor, catalogue.GetFile(descriptor.FormatId));
        Assert.True(descriptor.SupportedVersions[0].IsReadable);
        Assert.False(descriptor.SupportedVersions[0].IsWritable);
        Assert.Null(descriptor.WritableFormatVersion);
        Assert.Equal(7, descriptor.CurrentFormatVersion);
    }

    [Fact]
    public void Begin_RetiredDescriptor_DoesNotEmitPrefix()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var output = new IndexOutput(path);
            var exception = Assert.Throws<InvalidOperationException>(() =>
                CodecFileWriter.Begin(output, RetiredDescriptor()));

            Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, output.Position);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuiltInActiveDescriptors_ResolveExistingCurrentTargets()
    {
        foreach (var descriptor in CodecCatalog.Default.Files)
            Assert.Equal(descriptor.CurrentFormatVersion, descriptor.WritableFormatVersion);
    }

    [Fact]
    public void WriteAtomically_RepresentationPolicy_RejectsMissingIdentityBeforeSideEffects()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            bool bodyCalled = false;
            var exception = Assert.Throws<InvalidOperationException>(() =>
                CodecFileWriter.WriteAtomically(Path.Combine(directory, "data.active"),
                    ActiveDescriptor(new ExamplePolicy()), false, _ => bodyCalled = true));

            Assert.Contains("UnknownRepresentation", exception.Message, StringComparison.Ordinal);
            Assert.False(bodyCalled);
            Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteAtomically_RetiredDescriptor_RejectsBeforeBodyOrFileCreation()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            bool bodyCalled = false;
            var exception = Assert.Throws<InvalidOperationException>(() =>
                CodecFileWriter.WriteAtomically(Path.Combine(directory, "data.retired"),
                    RetiredDescriptor(), false, _ => bodyCalled = true));

            Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
            Assert.False(bodyCalled);
            Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CodecFileDescriptor RetiredDescriptor() => new(
        "example.retired.data", "example.retired", "Retired data",
        CodecFileMatcher.Extension(".retired"), 7,
        [new CodecVersionDescriptor(7, "retired-v7", migrationBehaviour: CodecMigrationBehaviour.Unsupported)],
        CodecAccessKind.Streaming, CodecFramingPolicy.Canonical, CodecChecksumPolicy.XxHash64,
        CodecMigrationBehaviour.Unsupported, [CodecFileMatcher.Extension(".retired.tmp")]);

    [Theory]
    [InlineData("supported", true)]
    [InlineData("retired", false)]
    [InlineData("unknown", false)]
    public void WriteAtomically_ExplicitRepresentation_EnforcesPolicy(string representation, bool allowed)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var descriptor = ActiveDescriptor(new ExamplePolicy());
            var identity = new CodecRepresentationIdentity(descriptor.FormatId, 8, representation);
            string path = Path.Combine(directory, "data.active");
            bool bodyCalled = false;
            void Write() => CodecFileWriter.WriteAtomically(path, descriptor, identity, false,
                body => { bodyCalled = true; body.WriteByte(42); });

            if (allowed)
            {
                Write();
                Assert.True(bodyCalled);
                Assert.True(File.Exists(path));
                Assert.Single(Directory.EnumerateFileSystemEntries(directory));
            }
            else
            {
                Assert.Throws<InvalidOperationException>(Write);
                Assert.False(bodyCalled);
                Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RetiredVersion_WithNewWriter_ReadsButCannotRewriteOrMigrate()
    {
        var descriptor = ActiveDescriptor();
        var old = new CodecRepresentationIdentity(descriptor.FormatId, 7);
        var current = new CodecRepresentationIdentity(descriptor.FormatId, 8);

        Assert.True(descriptor.Evaluate(CodecLifecycleOperation.Read, old).IsAllowed);
        Assert.True(descriptor.Evaluate(CodecLifecycleOperation.Create, target: current).IsAllowed);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.Create, target: old).IsAllowed);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.RewriteSource, old).IsAllowed);
        Assert.Equal("MigrationNotSupported",
            descriptor.Evaluate(CodecLifecycleOperation.Migrate, old, current).ReasonCode);
    }

    [Fact]
    public void SameVersionPolicy_RetiresOneRepresentation_IncludingNoOpMigration()
    {
        var descriptor = ActiveDescriptor(new ExamplePolicy());
        var supported = new CodecRepresentationIdentity(descriptor.FormatId, 8, "supported");
        var retired = supported with { RepresentationId = "retired" };

        Assert.True(descriptor.Evaluate(CodecLifecycleOperation.Read, retired).IsAllowed);
        Assert.True(descriptor.Evaluate(CodecLifecycleOperation.Create, target: supported).IsAllowed);
        Assert.True(descriptor.Evaluate(CodecLifecycleOperation.RewriteSource, supported).IsAllowed);
        Assert.True(descriptor.Evaluate(CodecLifecycleOperation.Migrate, supported, supported).IsAllowed);
        Assert.Equal("CodecReadOnly", descriptor.Evaluate(CodecLifecycleOperation.Create, target: retired).ReasonCode);
        Assert.Equal("CodecReadOnly", descriptor.Evaluate(CodecLifecycleOperation.RewriteSource, retired).ReasonCode);
        Assert.Equal("CodecReadOnly", descriptor.Evaluate(CodecLifecycleOperation.Migrate, retired, retired).ReasonCode);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.Read, supported with { RepresentationId = "unknown" }).IsAllowed);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.Create, target: supported with { RepresentationId = null }).IsAllowed);
    }

    [Fact]
    public void Policy_CannotGrantPermissionBeyondVersionCeilings()
    {
        var descriptor = ActiveDescriptor(new PermissivePolicy());
        var old = new CodecRepresentationIdentity(descriptor.FormatId, 7);
        var current = old with { Version = 8 };

        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.Create, target: old).IsAllowed);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.Migrate, old, current).IsAllowed);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.Read, old with { Version = 9 }).IsAllowed);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.Read, old with { FormatId = "another.format" }).IsAllowed);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.Read).IsAllowed);
        Assert.False(descriptor.Evaluate((CodecLifecycleOperation)999, old, current).IsAllowed);
    }

    private static CodecFileDescriptor ActiveDescriptor(ICodecLifecyclePolicy? policy = null) => new(
        "example.active.data", "example.active", "Active data", CodecFileMatcher.Extension(".active"), 8,
        [new CodecVersionDescriptor(7, "retired-v7", migrationBehaviour: CodecMigrationBehaviour.Unsupported),
         new CodecVersionDescriptor(8, "current-v8", isWritable: true, migrationBehaviour: CodecMigrationBehaviour.Reframe)],
        CodecAccessKind.Streaming, CodecFramingPolicy.Canonical, CodecChecksumPolicy.XxHash64,
        CodecMigrationBehaviour.Reframe, [CodecFileMatcher.Extension(".active.tmp")]) { LifecyclePolicy = policy };

    private sealed class ExamplePolicy : ICodecLifecyclePolicy
    {
        public CodecLifecycleDecision Evaluate(CodecLifecycleOperation operation,
            CodecRepresentationIdentity? source, CodecRepresentationIdentity? target)
        {
            foreach (var identity in new[] { source, target })
            {
                if (identity is not { } value)
                    continue;
                if (value.RepresentationId is not ("supported" or "retired"))
                    return CodecLifecycleDecision.Denied("UnknownRepresentation", "Unknown representation.");
                if (value.RepresentationId == "retired" && operation != CodecLifecycleOperation.Read)
                    return CodecLifecycleDecision.Denied("CodecReadOnly", "Retired representation.");
            }

            // This codec declares only a same-identity migration route.
            return operation != CodecLifecycleOperation.Migrate || source == target
                ? CodecLifecycleDecision.Allowed
                : CodecLifecycleDecision.Denied("MigrationNotSupported", "No declared route.");
        }
    }

    private sealed class PermissivePolicy : ICodecLifecyclePolicy
    {
        public CodecLifecycleDecision Evaluate(CodecLifecycleOperation operation,
            CodecRepresentationIdentity? source, CodecRepresentationIdentity? target) => CodecLifecycleDecision.Allowed;
    }
}
