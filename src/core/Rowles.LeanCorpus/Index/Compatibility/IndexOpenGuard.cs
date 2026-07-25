using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Compatibility;

internal static class IndexOpenGuard
{
    public static void EnsureNoBlockingMigration(MMapDirectory directory, IndexOpenCompatibilityMode mode)
    {
        if (mode == IndexOpenCompatibilityMode.UnsafeIgnoreCompatibility)
            return;

        if (IndexMigrationRecovery.HasBlockingMarker(directory.DirectoryPath))
            throw new InvalidDataException($"Index at '{directory.DirectoryPath}' has an incomplete migration marker. Roll back or abandon the migration before opening it.");
    }

    public static void EnsureCanOpenSegments(
        MMapDirectory directory,
        IEnumerable<string> segmentIds,
        IndexOpenCompatibilityMode mode,
        bool forWriting)
    {
        if (mode == IndexOpenCompatibilityMode.UnsafeIgnoreCompatibility)
            return;

        // Searchers validate the commit, segment metadata, required file presence,
        // and migration marker while opening. Codec headers are validated by the
        // relevant lazy component on first use. Writers retain the eager scan so
        // they cannot append to an index that requires migration.
        if (!forWriting)
            return;

        var migrationRecommended = false;
        foreach (var segmentId in segmentIds)
        {
            foreach (var filePath in FindSegmentFiles(directory.DirectoryPath, segmentId))
            {
                if (!TryReadSupportedVersion(filePath, out var version, out var currentVersion))
                    continue;

                if (version > currentVersion)
                    throw new InvalidDataException($"Index at '{directory.DirectoryPath}' uses unsupported future codec file '{Path.GetFileName(filePath)}' version {version}. Run a compatibility check before opening it.");

                if (version < currentVersion)
                    migrationRecommended = true;
            }
        }

        if (forWriting && migrationRecommended)
            throw new InvalidDataException($"Index at '{directory.DirectoryPath}' contains supported older codec files. Migrate the index before opening it for writing.");
    }

    private static IEnumerable<string> FindSegmentFiles(string directoryPath, string segmentId)
    {
        foreach (var file in FileOpenRetry.GetFiles(directoryPath, segmentId + ".*"))
            yield return file;
        foreach (var file in FileOpenRetry.GetFiles(directoryPath, segmentId + "_gen_*.del"))
            yield return file;
        foreach (var file in FileOpenRetry.GetFiles(directoryPath, segmentId + "_v_*.*"))
            yield return file;
    }

    private static bool TryReadSupportedVersion(string filePath, out byte version, out byte currentVersion)
    {
        version = 0;
        currentVersion = 0;
        var extension = GetCodecExtension(filePath);
        if (!CodecFormatTable.TryGet(extension, out var descriptor) ||
            !descriptor.HasHeader ||
            descriptor.CurrentVersion is null)
        {
            return false;
        }

        // Let IO / data-corruption exceptions propagate so that unreadable
        // segment files are not silently skipped during compatibility checks.
        using var stream = FileOpenRetry.OpenReadDelete(filePath);
        using var reader = new BinaryReader(stream);
        if (extension is ".pos")
        {
            version = PostingsFileHeader.ReadVersion(reader);
        }
        else if (extension is ".fdt" or ".fdx")
        {
            version = StoredFieldsFileHeader.ReadVersion(reader);
        }
        else
        {
            version = CodecFileHeader.ReadVersion(reader, descriptor.HeaderFormat!);
        }
        currentVersion = descriptor.CurrentVersion.Value;
        return true;
    }

    private static string GetCodecExtension(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".stats.json", StringComparison.OrdinalIgnoreCase))
            return ".stats";

        return Path.GetExtension(filePath);
    }
}
