namespace Rowles.LeanCorpus.Tests.Unit.Index.Migration;

/// <summary>
/// Unit tests for <see cref="IndexCodecMigrationAction"/> record.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "UnitTest")]
public sealed class IndexCodecMigrationActionTests
{
    [Fact(DisplayName = "IndexCodecMigrationAction: All required properties set correctly")]
    public void AllProperties_SetCorrectly()
    {
        var action = new IndexCodecMigrationAction
        {
            Kind = IndexCodecMigrationActionKind.RewriteFile,
            SourcePath = "/data/segment.pos",
            TargetPath = "/data/segment.pos.v2",
            Description = "Rewrite postings to v2 format",
            CanExecute = true,
            ReasonCannotExecute = null,
            SegmentId = "seg-001",
            FileName = "segment.pos",
            FromVersion = 1,
            ToVersion = 2,
        };

        Assert.Equal(IndexCodecMigrationActionKind.RewriteFile, action.Kind);
        Assert.Equal("/data/segment.pos", action.SourcePath);
        Assert.Equal("/data/segment.pos.v2", action.TargetPath);
        Assert.Equal("Rewrite postings to v2 format", action.Description);
        Assert.True(action.CanExecute);
        Assert.Null(action.ReasonCannotExecute);
        Assert.Equal("seg-001", action.SegmentId);
        Assert.Equal("segment.pos", action.FileName);
        Assert.Equal((byte)1, action.FromVersion);
        Assert.Equal((byte)2, action.ToVersion);
    }

    [Fact(DisplayName = "IndexCodecMigrationAction: CanExecute false with reason")]
    public void CanExecute_False_WithReason()
    {
        var action = new IndexCodecMigrationAction
        {
            Kind = IndexCodecMigrationActionKind.NoOp,
            SourcePath = "/data/empty",
            Description = "No action needed",
            CanExecute = false,
            ReasonCannotExecute = "Index is up to date",
        };

        Assert.False(action.CanExecute);
        Assert.Equal("Index is up to date", action.ReasonCannotExecute);
    }

    [Fact(DisplayName = "IndexCodecMigrationAction: Minimal required properties only")]
    public void MinimalProperties_SetCorrectly()
    {
        var action = new IndexCodecMigrationAction
        {
            Kind = IndexCodecMigrationActionKind.CopyFile,
            SourcePath = "/data/src",
            Description = "Copy source",
            CanExecute = true,
        };

        Assert.Equal(IndexCodecMigrationActionKind.CopyFile, action.Kind);
        Assert.Equal("/data/src", action.SourcePath);
        Assert.Equal("Copy source", action.Description);
        Assert.True(action.CanExecute);
        Assert.Null(action.TargetPath);
        Assert.Null(action.ReasonCannotExecute);
        Assert.Null(action.SegmentId);
        Assert.Null(action.FileName);
        Assert.Null(action.FromVersion);
        Assert.Null(action.ToVersion);
    }

    [Fact(DisplayName = "IndexCodecMigrationAction: DeleteTemporaryFile kind")]
    public void Kind_DeleteTemporaryFile()
    {
        var action = new IndexCodecMigrationAction
        {
            Kind = IndexCodecMigrationActionKind.DeleteTemporaryFile,
            SourcePath = "/data/tmp",
            Description = "Remove temp file",
            CanExecute = true,
        };

        Assert.Equal(IndexCodecMigrationActionKind.DeleteTemporaryFile, action.Kind);
    }

    [Fact(DisplayName = "IndexCodecMigrationAction: PublishCommit kind")]
    public void Kind_PublishCommit()
    {
        var action = new IndexCodecMigrationAction
        {
            Kind = IndexCodecMigrationActionKind.PublishCommit,
            SourcePath = "/data/segments.gen",
            Description = "Publish updated segment list",
            CanExecute = true,
        };

        Assert.Equal(IndexCodecMigrationActionKind.PublishCommit, action.Kind);
    }

    [Fact(DisplayName = "IndexCodecMigrationAction: WriteMarker kind")]
    public void Kind_WriteMarker()
    {
        var action = new IndexCodecMigrationAction
        {
            Kind = IndexCodecMigrationActionKind.WriteMarker,
            SourcePath = "/data/migration.mrk",
            Description = "Write migration marker",
            CanExecute = true,
        };

        Assert.Equal(IndexCodecMigrationActionKind.WriteMarker, action.Kind);
    }

    [Fact(DisplayName = "IndexCodecMigrationAction: Equal records are equal")]
    public void EqualRecords_AreEqual()
    {
        var a = new IndexCodecMigrationAction
        {
            Kind = IndexCodecMigrationActionKind.RewriteFile,
            SourcePath = "/data/a.pos",
            Description = "Rewrite",
            CanExecute = true,
        };
        var b = new IndexCodecMigrationAction
        {
            Kind = IndexCodecMigrationActionKind.RewriteFile,
            SourcePath = "/data/a.pos",
            Description = "Rewrite",
            CanExecute = true,
        };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact(DisplayName = "IndexCodecMigrationAction: Different SourcePath not equal")]
    public void DifferentSourcePath_NotEqual()
    {
        var a = new IndexCodecMigrationAction
        {
            Kind = IndexCodecMigrationActionKind.RewriteFile,
            SourcePath = "/data/a.pos",
            Description = "Rewrite",
            CanExecute = true,
        };
        var b = a with { SourcePath = "/data/b.pos" };

        Assert.NotEqual(a, b);
    }
}
