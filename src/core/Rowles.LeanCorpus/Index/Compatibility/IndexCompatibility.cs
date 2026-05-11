using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Compatibility;

/// <summary>
/// Checks whether a LeanCorpus index is readable, writable, or migratable by this build.
/// </summary>
public static class IndexCompatibility
{
    /// <summary>
    /// Checks compatibility for <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The index directory.</param>
    /// <param name="options">Compatibility options.</param>
    /// <returns>The compatibility result.</returns>
    public static IndexCompatibilityResult Check(MMapDirectory directory, IndexCompatibilityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        options ??= new IndexCompatibilityOptions();

        var inventory = IndexFormatInspector.Inspect(directory);
        var validation = IndexValidator.Check(directory, new IndexCheckOptions { Deep = options.DeepValidation });
        var migrationPlan = IndexCodecMigrator.Plan(inventory);

        var issues = new List<IndexCheckIssue>(migrationPlan.Inventory.Issues);
        foreach (var issue in validation.DetailedIssues)
        {
            if (!issues.Any(existing => existing.Code == issue.Code &&
                                        existing.FileName == issue.FileName &&
                                        existing.SegmentId == issue.SegmentId &&
                                        existing.Message == issue.Message))
            {
                issues.Add(issue);
            }
        }

        var hasNoCommit = validation.DetailedIssues.Count == 1 &&
                          validation.DetailedIssues[0].Code == IndexCheckIssueCodes.NoCommitFile;
        var hasValidationErrors = validation.DetailedIssues.Any(static issue =>
            issue.Severity == IndexCheckSeverity.Error &&
            issue.Code != IndexCheckIssueCodes.NoCommitFile);
        var hasMigrationActions = migrationPlan.Actions.Count > 0;

        var status = DetermineStatus(
            hasNoCommit,
            migrationPlan.Inventory.HasUnsupportedFutureFormat,
            hasValidationErrors,
            hasMigrationActions,
            migrationPlan.CanExecute,
            options);

        return new IndexCompatibilityResult
        {
            Status = status,
            CanRead = status is IndexCompatibilityStatus.Empty or IndexCompatibilityStatus.Compatible or IndexCompatibilityStatus.MigrationRecommended,
            CanWrite = status is IndexCompatibilityStatus.Empty or IndexCompatibilityStatus.Compatible,
            CanValidate = status is not IndexCompatibilityStatus.UnsupportedFutureFormat,
            CanMigrate = hasMigrationActions && migrationPlan.CanExecute,
            MustReject = status is IndexCompatibilityStatus.UnsupportedFutureFormat or IndexCompatibilityStatus.Corrupt,
            RequiresMigration = status is IndexCompatibilityStatus.MigrationRequired,
            Inventory = migrationPlan.Inventory,
            Issues = issues,
            MigrationActions = migrationPlan.Actions
        };
    }

    private static IndexCompatibilityStatus DetermineStatus(
        bool hasNoCommit,
        bool hasUnsupportedFutureFormat,
        bool hasValidationErrors,
        bool hasMigrationActions,
        bool canExecuteMigration,
        IndexCompatibilityOptions options)
    {
        if (hasNoCommit)
            return IndexCompatibilityStatus.Empty;
        if (hasUnsupportedFutureFormat)
            return IndexCompatibilityStatus.UnsupportedFutureFormat;
        if (hasValidationErrors)
            return IndexCompatibilityStatus.Corrupt;
        if (hasMigrationActions && (!options.AllowSupportedOlderFormats || options.RequireCurrentFormats || !canExecuteMigration))
            return IndexCompatibilityStatus.MigrationRequired;
        if (hasMigrationActions)
            return IndexCompatibilityStatus.MigrationRecommended;
        return IndexCompatibilityStatus.Compatible;
    }
}
