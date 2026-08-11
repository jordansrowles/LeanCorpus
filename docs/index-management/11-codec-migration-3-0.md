# Migrating indexes to 3.0

LeanCorpus 3.0 introduces a canonical self-identifying frame with checksums for current binary codec files. Supported 1.x and 2.x indexes remain readable, and the migration tooling can rewrite supported formats without a full reindex.

## Before upgrading

1. Stop every process that can write the index.
2. Create and verify a backup of the current commit.
3. Keep the old application binaries with that backup if rollback may be needed.
4. Run the 3.0 inspector and compatibility check before migration.

Older LeanCorpus releases do not understand files written with the 3.0 frame. A migrated or newly written 3.0 index must not be opened by a 2.x process.

## Inspect and check compatibility

```powershell
leancorpus-cli.exe inspect .\index
leancorpus-cli.exe compat .\index --deep
```

`Compatible` means every persistent file is current. `MigrationRecommended` means the index is readable but at least one supported file uses an older version or frame. `MigrationRequired` means the configured write policy will not open it until migration. Future, unknown or corrupt formats are rejected rather than guessed.

Compound segments are reported as logical members, including their physical `.cfs` location.

## Review a dry-run

Dry-run is the default and does not modify the index:

```powershell
leancorpus-cli.exe migrate .\index --dry-run --json --output .\migration-plan.json
```

Review every action. The plan distinguishes body-preserving reframes, body rewrites, coordinated family rewrites and unsupported paths. If any action is unsupported, restore or rebuild the affected data before proceeding.

## Execute the migration

Use a staging path on the same filesystem when possible:

```powershell
leancorpus-cli.exe migrate .\index --execute --staging .\index.migration
```

The migrator copies the selected commit into staging, writes current files through the same writers used by normal indexing and merging, deep-validates the result, repacks compound segments where necessary, and publishes the validated result. `migration_state.json` records recoverable progress.

Do not delete staging or recovery markers during an active or interrupted migration. Rerun the migrator after resolving the underlying failure.

## Validate the result

```powershell
leancorpus-cli.exe check .\index --deep
leancorpus-cli.exe inspect .\index
leancorpus-cli.exe compat .\index --deep
```

Current binary files should report Frame 1, their catalogue body-format version, xxHash64 and a valid checksum after deep checking. Search and stored-data smoke tests should return the same results as the source index.

## Rollback

Rollback means restoring the verified pre-migration backup. Do not copy selected 2.x files over a 3.0 commit or attempt an in-place downgrade. The commit manifest and all referenced segment files must come from one consistent backup generation.

## API workflow

```csharp
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Store;

using var directory = new MMapDirectory("./index");
var plan = IndexCodecMigrator.Plan(directory);

if (!plan.CanExecute)
    throw new InvalidOperationException("Migration plan contains unsupported actions.");

var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
{
    DryRun = false,
    StagingDirectory = "./index.migration",
    ValidateBeforeMigration = true,
    ValidateAfterMigration = true
});

if (!result.Succeeded)
    throw new InvalidDataException("Index migration failed validation.");
```

## See also

- [Backup and restore](08-backup-and-restore.md)
- [Validation and recovery](03-validation-recovery.md)
- [Index checker CLI](04-cli-checker.md)
