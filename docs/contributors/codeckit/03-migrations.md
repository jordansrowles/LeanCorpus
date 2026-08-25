# Codec migrations

Migration is a storage workflow built from the catalogue and normal current writers. It is not a second serialisation implementation.

## Migration behaviours

Each descriptor declares one behaviour:

| Behaviour | Meaning |
|---|---|
| `None` | The external or versionless representation needs no codec migration |
| `Reframe` | The body is unchanged and can be streamed into the current frame |
| `Rewrite` | Decode the old body and call the normal current writer |
| `CoordinatedRewrite` | Rewrite related family files together, such as `.fdt` plus `.fdx` |
| `Unsupported` | Inspection is available but rebuilding from source data is required |

Changing only legacy framing to canonical Frame v1 is normally a reframe. A body-layout change requires a rewrite.

## Operator workflow

Always inspect and back up before executing a migration.

```csharp
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Store;

using var directory = new MMapDirectory("./index");

var plan = IndexCodecMigrator.Plan(directory);
foreach (var action in plan.Actions)
    Console.WriteLine($"{action.Kind}: {action.Description}");

if (!plan.CanExecute)
    throw new InvalidOperationException("The index has an unsupported migration path.");

var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
{
    DryRun = false,
    StagingDirectory = "./index.migration"
});
```

The migrator stages a complete candidate, invokes normal current writers or reframers, validates it deeply, publishes it atomically, and records recovery state. An interrupted migration is detected on the next run.

## Writer monotonicity

Every current write path must be monotonic:

```text
legacy read -> direct write -> current
legacy read -> merge -> current
legacy read -> migration -> current
migration -> merge -> current
```

Tests must inspect emitted bytes and catalogue versions. A successful search result alone does not prove that a merge or migration avoided a format downgrade.

## Coordinated families

Stored fields and term vectors use paired data/index files. Rewrite the pair through the normal writer, validate offset monotonicity and body bounds, then publish both. Do not independently reframe one member if its offsets refer to the other's physical layout.

Third-party coordinated families register an `ICodecFamilyMigrationCoordinator` on the family descriptor. The coordinator receives bounded source bodies and staging outputs keyed by stable format ID. LeanCorpus invokes it once per family and segment, closes the staging outputs, wraps every result in its descriptor's current canonical frame, and publishes the files with the rest of the migrated segment.

Compound segments require logical member inspection, staged member rewrites and repacking. Validation after repacking must see the same format IDs and versions as loose files.

## Adding a version

When a body changes:

1. increment the catalogue's current body-format version;
2. keep the old version readable if support is promised;
3. mark only the newest version writable;
4. declare the old version's legacy framing and migration behaviour;
5. update the reader branch and normal writer;
6. add a historical fixture that was not produced by the current writer;
7. test direct, flush, merge and migration output;
8. add corruption and boundary cases;
9. update the on-disk format documentation.

Do not increment a body-format version merely because the outer frame changed.

## Backwards and downgrade policy

LeanCorpus 3.0 reads supported formats from 2.0 onwards and can migrate supported paths. Current writers emit canonical Frame v1. Older LeanCorpus releases do not understand that frame, so opening a 3.0-written index with a 2.x binary is unsupported. Keep a verified backup if rollback to an older application version may be required.

## See also

- [Adding persistent formats](02-adding-formats.md)
- [Validation and recovery](../../index-management/03-validation-recovery.md)
- [Index checker CLI](../../index-management/04-cli-checker.md)
