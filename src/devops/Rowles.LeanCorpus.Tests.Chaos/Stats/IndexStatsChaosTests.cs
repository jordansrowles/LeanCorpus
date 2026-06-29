using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Stats;

/// <summary>
/// Chaos tests for <see cref="IndexStats.WriteTo"/> covering the catch and finally blocks
/// that fire when the destination file is locked or read-only.
/// </summary>
[Trait("Category", "Chaos")]
[Trait("Category", "Stats")]
public sealed class IndexStatsChaosTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public IndexStatsChaosTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    private static IndexStats BuildStats() => new(
        totalDocCount: 50,
        liveDocCount: 50,
        avgFieldLengths: new(StringComparer.Ordinal) { ["body"] = 8.0f },
        fieldDocCounts: new(StringComparer.Ordinal) { ["body"] = 50 },
        fieldLengthSums: new(StringComparer.Ordinal) { ["body"] = 400L });

    /// <summary>
    /// When the destination file exists and is read-only, <see cref="File.Move"/> throws
    /// <see cref="UnauthorizedAccessException"/>. The when-clause <c>File.Exists(path)</c>
    /// is true, so the catch block fires silently and the method returns without throwing.
    /// The finally block then deletes the tmp file.
    /// </summary>
    [Fact(DisplayName = "IndexStats.WriteTo: ReadOnly Destination Fires UnauthorisedException Catch And Cleans Up Tmp")]
    public void WriteTo_ReadOnlyDestination_FiresUnauthorisedCatchAndCleansTmp()
    {
        var path = Path.Combine(_fixture.Path, $"stats_ro_{Guid.NewGuid():N}.json");

        // Write once so the file exists.
        BuildStats().WriteTo(path);

        // Make the destination read-only so File.Move will fail.
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            // Must not throw; the catch block absorbs the UnauthorisedException.
            var exception = Record.Exception(() => BuildStats().WriteTo(path));
            Assert.Null(exception);

            // The original stats file must still be present (the catch kept it).
            Assert.True(File.Exists(path));

            // No leftover .tmp files should remain (the finally block deleted them).
            var tmpFiles = Directory.GetFiles(_fixture.Path, "*.tmp");
            Assert.Empty(tmpFiles);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// When the destination file is held open for exclusive read, <see cref="File.Move"/> throws
    /// <see cref="IOException"/>. The when-clause fires and the method returns without throwing.
    /// </summary>
    [Fact(DisplayName = "IndexStats.WriteTo: File Locked For Read Fires IOException Catch")]
    public void WriteTo_FileLockedForRead_FiresIoExceptionCatch()
    {
        var path = Path.Combine(_fixture.Path, $"stats_locked_{Guid.NewGuid():N}.json");

        // Write once so the file exists.
        BuildStats().WriteTo(path);

        // Hold the file open so File.Move fails with IOException.
        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var exception = Record.Exception(() => BuildStats().WriteTo(path));
        Assert.Null(exception);

        // The file must still be reachable.
        Assert.True(File.Exists(path));
    }
}
