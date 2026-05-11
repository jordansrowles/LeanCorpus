using Rowles.LeanCorpus.Index.Migration;
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
        foreach (var file in Directory.GetFiles(directoryPath, segmentId + ".*"))
            yield return file;
        foreach (var file in Directory.GetFiles(directoryPath, segmentId + "_gen_*.del"))
            yield return file;
        foreach (var file in Directory.GetFiles(directoryPath, segmentId + "_v_*.*"))
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

        try
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != Codecs.CodecConstants.Magic)
                return false;

            version = reader.ReadByte();
            currentVersion = descriptor.CurrentVersion.Value;
            return true;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string GetCodecExtension(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".stats.json", StringComparison.OrdinalIgnoreCase))
            return ".stats";

        return Path.GetExtension(filePath);
    }
}
