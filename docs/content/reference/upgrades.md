# Upgrade guide

Read the release changelog before upgrading a deployed index.

1. Check the target package and runtime versions in [Compatibility](compatibility.md).
2. Read the release entry for removed APIs, index-format changes and codec migrations.
3. Back up the index or retain a known-good commit before migration.
4. Test opening, searching and writing a copy of production data.
5. Deploy application and package changes together, then monitor recovery and merge diagnostics.

Use [CodecKit migrations](../contributors/codeckit/03-migrations.md) for binary-format work and [Changelog](../changelog/index.md) for release notes.

## Upgrading 3.1.1 indexes to 4.0

LeanCorpus 4.0 reads supported 3.x indexes and can migrate them through
`IndexCodecMigrator`. Run compatibility inspection and a dry-run plan on a copy
before executing migration. Keep the original backup until the migrated copy
has passed deep validation and application search checks. Migration publishes
its new commit after its referenced files are durable; retained older commits
remain available until the normal retention policy removes them.

The current 4.0 writer uses stored-fields body v5 and Sorted DocValues v3.
Readers retain stored-fields v1-v4 support, including the intermediate v4
layout. A 3.1.1 fixture therefore migrates stored fields from v3 directly to
v5 and Sorted DocValues from v2 to v3. The stored-fields data and index files
are rewritten together. Legacy segment deletion state is recorded explicitly
in the migrated commit, preserving the original live-document set.

Downgrading an index written or migrated by 4.x to a 3.x reader is unsupported.
To roll back an application deployment, restore the preserved 3.x backup rather
than opening the 4.x output with the older writer. Rowles.Text has its own
package version and release boundary.

## Rebuilding legacy BBQ as BBQ v3

CodecKit migration preserves legacy BBQ v1/v2 payloads and their historical
scoring. It cannot reconstruct the original per-component magnitudes or create
the canonical Int8 records required by BBQ v3. After migration, legacy BBQ
segments remain searchable, but a 4.0 writer rejects mutation and automatic
merging of an index containing them.

To create a BBQ v3 index, retain a backup of the old index and rebuild from the
original documents and embeddings. Configure `VectorQuantisation.BBQ` with
`NormaliseVectors = true`; every BBQ field must have at least 128 dimensions.
The writer rejects unsupported fields and does not fall back to standalone
Int8. Imports from Float32 are quantised once into the canonical representation;
imports from standalone Int8 are a further lossy conversion. Do not use the
legacy `.vq` files as an input source for this rebuild.

BBQ v3 scores are approximate to the original Float32 cosine. ADR042 remains
Proposed because the fixed integration recall gate has not passed on the current
implementation; this branch does not establish release qualification. Validate
the target workload and wait for the ADR's required quality and performance
gates before deploying BBQ v3.
