using System.Buffers;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index;

internal static class IndexFileInspector
{
    public static List<(int Generation, string FilePath)> FindCommitFiles(string directoryPath)
    {
        var result = new List<(int Generation, string FilePath)>();
        if (!Directory.Exists(directoryPath))
            return result;

        foreach (var file in Directory.GetFiles(directoryPath, "segments_*"))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".tmp", StringComparison.Ordinal))
                continue;

            var genStr = fileName.AsSpan("segments_".Length);
            if (int.TryParse(genStr, out int generation))
                result.Add((generation, file));
        }

        result.Sort(static (left, right) => right.Generation.CompareTo(left.Generation));
        return result;
    }

    public static CommitData? TryReadCommit(string commitPath, int generation, IndexCheckResult result)
    {
        string? json;
        try
        {
            json = CommitFileFormat.TryReadJson(commitPath);
        }
        catch (IOException ex)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.CommitUnreadable,
                $"Cannot read commit file '{commitPath}': {ex.Message}",
                Path.GetFileName(commitPath),
                null,
                false);
            return null;
        }

        if (json is null)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.CommitCrcMismatch,
                $"Commit file '{commitPath}' failed CRC validation.",
                Path.GetFileName(commitPath),
                null,
                false);
            return null;
        }

        CommitData? commitData;
        try
        {
            commitData = JsonSerializer.Deserialize(json, LeanCorpusJsonContext.Default.CommitData);
        }
        catch (JsonException ex)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.CommitInvalidJson,
                $"Commit file '{commitPath}' contains invalid JSON: {ex.Message}",
                Path.GetFileName(commitPath),
                null,
                false);
            return null;
        }

        if (commitData is null)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.CommitInvalidJson,
                $"Commit file '{commitPath}' deserialised to null.",
                Path.GetFileName(commitPath),
                null,
                false);
            return null;
        }

        try
        {
            commitData.Validate();
        }
        catch (InvalidDataException ex)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.CommitInvalidJson,
                $"Commit file '{commitPath}' failed validation: {ex.Message}",
                Path.GetFileName(commitPath),
                null,
                false);
            return null;
        }

        if (commitData.Generation != generation)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.CommitGenerationMismatch,
                $"Commit file '{commitPath}' declares generation {commitData.Generation}, expected {generation}.",
                Path.GetFileName(commitPath),
                null,
                false);
            return null;
        }

        return commitData;
    }

    public static bool CheckRequiredFile(string filePath, string segmentId, IndexCheckResult result)
    {
        result.FilesChecked++;
        var fileName = Path.GetFileName(filePath);
        var info = new FileInfo(filePath);
        if (!info.Exists)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.RequiredFileMissing,
                $"Segment '{segmentId}' is missing required file '{fileName}'.",
                fileName,
                segmentId,
                true);
            return false;
        }

        if (info.Length == 0)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.RequiredFileEmpty,
                $"Segment '{segmentId}' has empty required file '{fileName}'.",
                fileName,
                segmentId,
                false);
            return false;
        }

        return true;
    }

    public static bool CheckOptionalFile(string filePath, string segmentId, IndexCheckResult result)
    {
        if (!File.Exists(filePath))
            return false;

        result.FilesChecked++;
        return true;
    }

    public static void CheckCodecHeader(
        string filePath,
        byte maxVersion,
        ICodec<byte[]> format,
        string fileType,
        string segmentId,
        IndexCheckResult result)
    {
        if (!File.Exists(filePath))
            return;

        result.FilesChecked++;
        var fileName = Path.GetFileName(filePath);
        try
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: false);
            byte version = CodecFileHeader.ReadVersion(reader, format);
            if (version > maxVersion)
            {
                result.AddIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.UnsupportedCodecVersion,
                    $"Unsupported {fileType} format version {version}; this build supports up to version {maxVersion}.",
                    fileName,
                    segmentId,
                    false);
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
        {
            result.AddIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidCodecMagic,
                $"Cannot read {fileType} header from '{fileName}': {ex.Message}",
                fileName,
                segmentId,
                false);
        }
    }
}