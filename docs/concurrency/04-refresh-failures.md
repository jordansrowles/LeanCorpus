# Refresh failures

`SearcherManager` polls for committed generations in the background. If refresh cannot inspect or open a new commit, it retains the current healthy searcher and records the failure.

Search availability can therefore remain healthy while freshness is degraded. Monitor both.

## Subscribe to failures

```csharp
manager.RefreshFailed += (_, e) =>
{
    logger.LogWarning(
        e.Error,
        "Searcher refresh failed {ConsecutiveFailures} time(s)",
        e.ConsecutiveFailures);
};
```

The event is diagnostic. Do not dispose or replace the manager from the event handler.

## Poll health

```csharp
if (manager.LastRefreshError is { } error)
{
    logger.LogWarning(
        error,
        "Last refresh failed at {FailureTime}; consecutive failures: {Count}",
        manager.LastRefreshErrorAt,
        manager.ConsecutiveRefreshFailures);
}
```

`ConsecutiveRefreshFailures` resets after a successful refresh.

Track the age of the latest successfully observed generation as well as the error count. One isolated retryable failure is different from a manager serving an old commit for several minutes.

## Common causes

| Cause | What to check |
|---|---|
| Commit still becoming visible on unusual storage | Filesystem atomic-rename and directory visibility semantics |
| Transient sharing or antivirus interference | Platform logs and whether `FileOpenRetry` exhausted its bounded policy |
| Missing segment file | Commit integrity, external cleanup, restore procedure |
| Corrupt commit or codec data | Run index validation and inspect the selected generation |
| Permissions changed | Service identity, directory traversal, and file read permissions |
| File-descriptor exhaustion | Process limits, segment count, and active searcher or snapshot lifetime |
| Incompatible format | `CompatibilityMode`, format inventory, and migration plan |

## Recovery behaviour

The manager does not switch to a partially opened searcher. Existing leases remain valid and new acquisitions receive the last successfully published searcher.

After the underlying condition clears, a later background poll or `MaybeRefresh()` can succeed:

```csharp
try
{
    bool changed = manager.MaybeRefresh();
    logger.LogInformation("Refresh completed; changed={Changed}", changed);
}
catch (Exception ex)
{
    logger.LogError(ex, "Explicit refresh attempt failed");
}
```

Do not loop explicit refresh without delay. The background interval already provides bounded retries, and a tight loop can amplify storage failure.

## Escalation

- Alert on repeated failures or freshness age, not every isolated event.
- Preserve the current manager while it can serve valid searches.
- Stop the writer if validation indicates ongoing corruption or missing files.
- Use [validation and recovery](../index-management/03-validation-recovery.md) to select a valid commit.
- Restore into a fresh directory if the local index cannot be recovered.

See [Searcher manager](01-searcher-manager.md), [Backup and restore](../index-management/08-backup-and-restore.md), and [Production deployment](../tips/04-production-deployment.md).
