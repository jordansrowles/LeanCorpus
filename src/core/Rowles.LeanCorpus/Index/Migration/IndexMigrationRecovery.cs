using System.Text.Json;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Migration;

/// <summary>
/// Reads and clears migration markers left by interrupted index migrations.
/// </summary>
public static class IndexMigrationRecovery
{
    /// <summary>The migration marker file name.</summary>
    public const string MarkerFileName = "migration_state.json";

    private const string TemporaryMarkerFileName = "migration_state.json.tmp";

    /// <summary>
    /// Gets the current migration marker state for <paramref name="indexPath"/>.
    /// </summary>
    /// <param name="indexPath">The index directory path.</param>
    /// <returns>The migration marker, or a marker with state <see cref="IndexMigrationState.None"/>.</returns>
    public static IndexMigrationMarker GetState(string indexPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
        var markerPath = Path.Combine(indexPath, MarkerFileName);
        if (!File.Exists(markerPath))
        {
            return new IndexMigrationMarker
            {
                State = IndexMigrationState.None,
                SourceDirectory = indexPath,
                StagingDirectory = string.Empty,
                SourceCommitGeneration = null,
                CreatedAtUtc = DateTimeOffset.MinValue,
                UpdatedAtUtc = DateTimeOffset.MinValue,
                PlannedActions = []
            };
        }

        try
        {
            var json = File.ReadAllText(markerPath);
            return JsonSerializer.Deserialize(json, LeanCorpusJsonContext.Default.IndexMigrationMarker)
                ?? throw new InvalidDataException($"Migration marker '{markerPath}' deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Migration marker '{markerPath}' is not valid.", ex);
        }
    }

    /// <summary>
    /// Rolls back an interrupted migration before publication by deleting only marker and staging files.
    /// </summary>
    /// <param name="indexPath">The source index directory path.</param>
    /// <exception cref="InvalidOperationException">Thrown when the migration has reached publish and the staging directory must be preserved.</exception>
    public static void RollBack(string indexPath)
    {
        var marker = GetState(indexPath);
        if (marker.State is IndexMigrationState.ReadyToPublish or IndexMigrationState.Published)
            throw new InvalidOperationException($"Migration is in state {marker.State}; staging data is preserved. Abandon the marker only after confirming the source index is safe.");

        if (marker.State == IndexMigrationState.Failed &&
            !string.IsNullOrWhiteSpace(marker.StagingDirectory) &&
            Directory.Exists(marker.StagingDirectory))
        {
            throw new InvalidOperationException("Migration failed after staging data was created; staging data is preserved. Abandon the marker only after confirming the source index is safe.");
        }

        if (!string.IsNullOrWhiteSpace(marker.StagingDirectory) &&
            Directory.Exists(marker.StagingDirectory) &&
            !PathsEqual(marker.StagingDirectory, indexPath))
        {
            FileOpenRetry.DeleteDirectory(marker.StagingDirectory, recursive: true);
        }

        DeleteMarkerFiles(indexPath);
    }

    /// <summary>
    /// Abandons an interrupted migration marker without deleting the staging directory.
    /// </summary>
    /// <param name="indexPath">The source index directory path.</param>
    public static void Abandon(string indexPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
        DeleteMarkerFiles(indexPath);
    }

    internal static bool HasBlockingMarker(string indexPath)
    {
        var state = GetState(indexPath).State;
        return state is not IndexMigrationState.None and not IndexMigrationState.Published;
    }

    internal static void WriteMarker(string indexPath, IndexMigrationMarker marker, bool durable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
        ArgumentNullException.ThrowIfNull(marker);
        Directory.CreateDirectory(indexPath);
        var json = JsonSerializer.Serialize(marker, LeanCorpusJsonContext.Default.IndexMigrationMarker);
        Store.IndexAtomicFileWriter.WriteText(Path.Combine(indexPath, MarkerFileName), json, durable);
    }

    private static void DeleteMarkerFiles(string indexPath)
    {
        FileOpenRetry.Delete(Path.Combine(indexPath, MarkerFileName));
        FileOpenRetry.Delete(Path.Combine(indexPath, TemporaryMarkerFileName));
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
