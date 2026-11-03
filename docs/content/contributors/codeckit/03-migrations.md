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

## LeanCorpus 4.0 vector migrations

Float `.vec` v1 bodies migrate to v2 by structural rewrite. Their owning
`.seg` metadata must declare a persisted HNSW graph for the field. The migrator
validates graph membership, unique document IDs and document bounds, and inserts
the reconstructed presence bitmap. Dense Float32 bytes remain unchanged.

Int8 `.vq` v1 and v2 bodies migrate to v3 by adding the v3 storage-kind tag and
preserving the encoded payload. For v1, the migrator reconstructs authoritative
presence from the persisted HNSW graph, after validating membership, unique IDs
and document bounds. For v2, it validates and copies the persisted presence
bitmap; this path does not require HNSW access or rebuild presence from graph
membership. Int8 values and parameters are copied without dequantisation or
requantisation. The HNSW body stays at v1 and its scorer generation is resolved
from the associated `.vq` storage kind.

BBQ-tagged `.vq` v1/v2 bodies have no migration route. Migration of any BBQ
format, including a current-version no-op, is rejected before changing source
files. Historical BBQ bodies remain readable and searchable with their existing
scorers. Any committed BBQ field makes the entire containing index read-only to
writers, including mixed indexes and all-deleted fields. New output is Float32
or Int8 only. To continue indexing, create a separate index from original
documents and embeddings; decoded BBQ data does not restore the original
components.

The `.vec` v1 and Int8 `.vq` v1 presence rewrites require persisted HNSW
membership. Without it, planning reports `UnsupportedMigrationPath` and
migration stops before changing source files. Non-zero vector contents are not
presence evidence.

Compound members use the same staged rewrite and normal repacking protocol.
Publication failure preserves the last committed source index and supports retry.

Frozen loose and compound release fixtures are generated by the published
LeanCorpus 3.1.1 package. Their manifests record package and assembly hashes,
committed deletions, vector-bearing IDs and every physical file hash. Offline
acceptance verifies stored fields, DocValues, liveness, search, sparse vectors
and HNSW membership through planning, migration, reopen and force merge. Both
layouts physically sort on `sort:id`, preserving released metadata
`String:sort:id:False` and sorted search results throughout. Acceptance checks
the complete expected codec action set, with no index-sort rewrite.
Focused vector tests additionally cover Int8 structural migration and BBQ
migration denials. The resulting `.vec` v2 and `.vq` v3 formats are one-way
persisted-format boundaries; a 3.1.1 reader is not expected to open them.

Regenerate deliberately with `./devops data fixtures --version 3.1.1
--scenario migration` using the pinned SDK and the isolated package source.
`./devops data fixtures --list` also lists the sparse HNSW migration scenario.
The deprecated `data migration-fixtures-311` alias remains during migration to
the catalog command. Ordinary tests never invoke a generator or download the
historical package. See `src/devops/tests/fixtures/historical/README.md` for both scenarios,
package and assembly pins, and archive generation details.
