using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Migration;

namespace Rowles.LeanCorpus.Cli;

internal sealed record CheckRequest(
    string IndexPath,
    IndexCheckOptions Options,
    bool Json,
    bool SummaryOnly,
    bool FailOnWarnings,
    string? OutputPath)
{
    public static CheckRequest Empty { get; } = new(
        string.Empty,
        new IndexCheckOptions(),
        Json: false,
        SummaryOnly: false,
        FailOnWarnings: false,
        OutputPath: null);
}

internal static class CliExitCodes
{
    public const int Success = 0;
    public const int ValidationErrors = 1;
    public const int InvalidArguments = 2;
}

internal sealed class CliIndexCheckResultDto
{
    public required bool IsHealthy { get; init; }
    public int? CommitGeneration { get; init; }
    public required int SegmentsChecked { get; init; }
    public required int DocumentsChecked { get; init; }
    public required int FilesChecked { get; init; }
    public required List<CliIndexCheckIssueDto> Issues { get; init; }

    public static CliIndexCheckResultDto FromResult(IndexCheckResult result)
    {
        var issues = new List<CliIndexCheckIssueDto>(result.DetailedIssues.Count);
        foreach (var issue in result.DetailedIssues)
            issues.Add(CliIndexCheckIssueDto.FromIssue(issue));

        return new CliIndexCheckResultDto
        {
            IsHealthy = result.IsHealthy,
            CommitGeneration = result.CommitGeneration,
            SegmentsChecked = result.SegmentsChecked,
            DocumentsChecked = result.DocumentsChecked,
            FilesChecked = result.FilesChecked,
            Issues = issues
        };
    }
}

internal sealed class CliIndexCheckIssueDto
{
    public required string Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? FileName { get; init; }
    public string? SegmentId { get; init; }
    public required bool IsRepairable { get; init; }
    public required IReadOnlyList<string> SuggestedActions { get; init; }

    public static CliIndexCheckIssueDto FromIssue(IndexCheckIssue issue)
        => new()
        {
            Severity = issue.Severity.ToString(),
            Code = issue.Code,
            Message = issue.Message,
            FileName = issue.FileName,
            SegmentId = issue.SegmentId,
            IsRepairable = issue.IsRepairable,
            SuggestedActions = issue.SuggestedActions
        };
}

internal sealed class CliIndexFormatInventoryDto
{
    public required string DirectoryPath { get; init; }
    public int? CommitGeneration { get; init; }
    public long? ContentToken { get; init; }
    public required IReadOnlyList<string> SegmentIds { get; init; }
    public required List<CliSegmentFormatInventoryDto> Segments { get; init; }
    public required List<CliCodecFileInventoryDto> OrphanFiles { get; init; }
    public required List<CliIndexCheckIssueDto> Issues { get; init; }
    public required bool HasUnsupportedFutureFormat { get; init; }

    public static CliIndexFormatInventoryDto FromInventory(IndexFormatInventory inventory)
        => new()
        {
            DirectoryPath = inventory.DirectoryPath,
            CommitGeneration = inventory.CommitGeneration,
            ContentToken = inventory.ContentToken,
            SegmentIds = inventory.SegmentIds,
            Segments = inventory.Segments.Select(CliSegmentFormatInventoryDto.FromSegment).ToList(),
            OrphanFiles = inventory.OrphanFiles.Select(CliCodecFileInventoryDto.FromFile).ToList(),
            Issues = inventory.Issues.Select(CliIndexCheckIssueDto.FromIssue).ToList(),
            HasUnsupportedFutureFormat = inventory.HasUnsupportedFutureFormat
        };
}

internal sealed class CliSegmentFormatInventoryDto
{
    public required string SegmentId { get; init; }
    public int? DocCount { get; init; }
    public int? LiveDocCount { get; init; }
    public int? CommitGeneration { get; init; }
    public int? DelGeneration { get; init; }
    public required List<CliCodecFileInventoryDto> Files { get; init; }
    public required IReadOnlyList<string> MissingFiles { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public static CliSegmentFormatInventoryDto FromSegment(SegmentFormatInventory segment)
        => new()
        {
            SegmentId = segment.SegmentId,
            DocCount = segment.DocCount,
            LiveDocCount = segment.LiveDocCount,
            CommitGeneration = segment.CommitGeneration,
            DelGeneration = segment.DelGeneration,
            Files = segment.Files.Select(CliCodecFileInventoryDto.FromFile).ToList(),
            MissingFiles = segment.MissingFiles,
            Warnings = segment.Warnings
        };
}

internal sealed class CliCodecFileInventoryDto
{
    public required string FileName { get; init; }
    public required string Extension { get; init; }
    public required string CodecName { get; init; }
    public string? FormatId { get; init; }
    public string? FamilyId { get; init; }
    public required string FrameKind { get; init; }
    public int? FrameVersion { get; init; }
    public int? FormatVersion { get; init; }
    public int? CurrentFormatVersion { get; init; }
    public string? ChecksumAlgorithm { get; init; }
    public required string ChecksumStatus { get; init; }
    public required string PhysicalLocation { get; init; }
    public string? CompoundFileName { get; init; }
    public byte? Version { get; init; }
    public byte? CurrentVersion { get; init; }
    public required bool HasValidMagic { get; init; }
    public required bool IsSupported { get; init; }
    public required bool IsCurrent { get; init; }
    public long? Length { get; init; }
    public string? SegmentId { get; init; }
    public string? FieldName { get; init; }

    public static CliCodecFileInventoryDto FromFile(CodecFileInventory file)
        => new()
        {
            FileName = file.FileName,
            Extension = file.Extension,
            CodecName = file.CodecName,
            FormatId = file.FormatId,
            FamilyId = file.FamilyId,
            FrameKind = file.FrameKind.ToString(),
            FrameVersion = file.FrameVersion,
            FormatVersion = file.FormatVersion,
            CurrentFormatVersion = file.CurrentFormatVersion,
            ChecksumAlgorithm = file.ChecksumAlgorithm?.ToString(),
            ChecksumStatus = file.ChecksumStatus.ToString(),
            PhysicalLocation = file.PhysicalLocation.ToString(),
            CompoundFileName = file.CompoundFileName,
            Version = file.Version,
            CurrentVersion = file.CurrentVersion,
            HasValidMagic = file.HasValidMagic,
            IsSupported = file.IsSupported,
            IsCurrent = file.IsCurrent,
            Length = file.Length,
            SegmentId = file.SegmentId,
            FieldName = file.FieldName
        };
}

internal sealed class CliCompatibilityResultDto
{
    public required string Status { get; init; }
    public required bool CanRead { get; init; }
    public required bool CanWrite { get; init; }
    public required bool CanValidate { get; init; }
    public required bool CanMigrate { get; init; }
    public required bool MustReject { get; init; }
    public required bool RequiresMigration { get; init; }
    public required List<CliIndexCheckIssueDto> Issues { get; init; }
    public required List<CliMigrationActionDto> MigrationActions { get; init; }

    public static CliCompatibilityResultDto FromResult(IndexCompatibilityResult result)
        => new()
        {
            Status = result.Status.ToString(),
            CanRead = result.CanRead,
            CanWrite = result.CanWrite,
            CanValidate = result.CanValidate,
            CanMigrate = result.CanMigrate,
            MustReject = result.MustReject,
            RequiresMigration = result.RequiresMigration,
            Issues = result.Issues.Select(CliIndexCheckIssueDto.FromIssue).ToList(),
            MigrationActions = result.MigrationActions.Select(CliMigrationActionDto.FromAction).ToList()
        };
}

internal sealed class CliMigrationResultDto
{
    public required bool Succeeded { get; init; }
    public required bool DryRun { get; init; }
    public required string SourceDirectory { get; init; }
    public string? StagingDirectory { get; init; }
    public required List<CliMigrationActionDto> Actions { get; init; }
    public required List<CliIndexCheckIssueDto> Issues { get; init; }

    public static CliMigrationResultDto FromPlan(IndexCodecMigrationPlan plan, string sourceDirectory, string? stagingDirectory)
        => new()
        {
            Succeeded = plan.CanExecute,
            DryRun = true,
            SourceDirectory = sourceDirectory,
            StagingDirectory = stagingDirectory,
            Actions = plan.Actions.Select(CliMigrationActionDto.FromAction).ToList(),
            Issues = plan.Issues.Select(CliIndexCheckIssueDto.FromIssue).ToList()
        };

    public static CliMigrationResultDto FromResult(IndexCodecMigrationResult result)
        => new()
        {
            Succeeded = result.Succeeded,
            DryRun = result.DryRun,
            SourceDirectory = result.SourceDirectory,
            StagingDirectory = result.StagingDirectory,
            Actions = result.ExecutedActions.Select(CliMigrationActionDto.FromAction).ToList(),
            Issues = result.Issues.Select(CliIndexCheckIssueDto.FromIssue).ToList()
        };
}

internal sealed class CliMigrationActionDto
{
    public required string Kind { get; init; }
    public required string SourcePath { get; init; }
    public string? TargetPath { get; init; }
    public required string Description { get; init; }
    public required bool CanExecute { get; init; }
    public string? ReasonCannotExecute { get; init; }
    public string? SegmentId { get; init; }
    public string? FileName { get; init; }
    public byte? FromVersion { get; init; }
    public byte? ToVersion { get; init; }

    public static CliMigrationActionDto FromAction(IndexCodecMigrationAction action)
        => new()
        {
            Kind = action.Kind.ToString(),
            SourcePath = action.SourcePath,
            TargetPath = action.TargetPath,
            Description = action.Description,
            CanExecute = action.CanExecute,
            ReasonCannotExecute = action.ReasonCannotExecute,
            SegmentId = action.SegmentId,
            FileName = action.FileName,
            FromVersion = action.FromVersion,
            ToVersion = action.ToVersion
        };
}

internal sealed class CliBackupResultDto
{
    public required int CommitGeneration { get; init; }
    public required long ContentToken { get; init; }
    public required string BackupDirectoryPath { get; init; }
    public required IReadOnlyList<string> CopiedFiles { get; init; }
    public required List<CliBackupFileDto> Files { get; init; }

    public static CliBackupResultDto FromResult(IndexBackupResult result)
        => new()
        {
            CommitGeneration = result.Manifest.CommitGeneration,
            ContentToken = result.Manifest.ContentToken,
            BackupDirectoryPath = result.BackupDirectoryPath,
            CopiedFiles = result.CopiedFiles,
            Files = result.Manifest.Files.Select(CliBackupFileDto.FromEntry).ToList()
        };
}

internal sealed class CliRestoreResultDto
{
    public required int CommitGeneration { get; init; }
    public required long ContentToken { get; init; }
    public required string TargetDirectoryPath { get; init; }
    public required IReadOnlyList<string> RestoredFiles { get; init; }
    public required bool IsHealthy { get; init; }
    public required List<CliIndexCheckIssueDto> Issues { get; init; }

    public static CliRestoreResultDto FromResult(IndexRestoreResult result)
        => new()
        {
            CommitGeneration = result.Manifest.CommitGeneration,
            ContentToken = result.Manifest.ContentToken,
            TargetDirectoryPath = result.TargetDirectoryPath,
            RestoredFiles = result.RestoredFiles,
            IsHealthy = result.ValidationResult?.IsHealthy ?? true,
            Issues = result.ValidationResult?.DetailedIssues.Select(CliIndexCheckIssueDto.FromIssue).ToList() ?? []
        };
}

internal sealed class CliBackupFileDto
{
    public required string FileName { get; init; }
    public required long Length { get; init; }
    public required string Crc32 { get; init; }
    public string? SegmentId { get; init; }
    public required string Role { get; init; }
    public required bool IsRequired { get; init; }
    public required bool IsCommitFile { get; init; }

    public static CliBackupFileDto FromEntry(IndexBackupFileEntry entry)
        => new()
        {
            FileName = entry.FileName,
            Length = entry.Length,
            Crc32 = entry.Crc32.ToString("x8"),
            SegmentId = entry.SegmentId,
            Role = entry.Role,
            IsRequired = entry.IsRequired,
            IsCommitFile = entry.IsCommitFile
        };
}
