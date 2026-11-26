# Upgrade guide

Read the release changelog before upgrading a deployed index.

1. Check the target package and runtime versions in [Compatibility](compatibility.md).
2. Read the release entry for removed APIs, index-format changes and codec migrations.
3. Back up the index or retain a known-good commit before migration.
4. Test opening, searching and writing a copy of production data.
5. Deploy application and package changes together, then monitor recovery and merge diagnostics.

Use [CodecKit migrations](../contributors/codeckit/03-migrations.md) for binary-format work and [Changelog](../changelog/index.md) for release notes: [LeanCorpus 4.0.0](<../changelog/engine/4.0.0 - 2026-11-26.md>) and, for standalone analysis, [Rowles.Text 2.0.0](<../changelog/text/2.0.0 - 2026-11-26.md>).

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

## BBQ-containing indexes in 4.0

LeanCorpus 4.0 keeps historical BBQ data readable and searchable with its
existing scorer. Any committed BBQ field makes the containing index read-only:
writer opening, mutation, import, merge and migration are rejected before
persistent changes, including a no-op migration. This applies to mixed indexes
and BBQ fields whose documents have all been deleted.

New vector output is Float32, searched exactly unless the opt-in Float32 vector
graph (`BuildHnswOnFlush`) is enabled; an index with `.vgr` graph files needs
4.0 or later. New Int8 and `.hnsw` graph writes are retired. Historical Int8 vectors remain readable and use exact scoring on
their decoded stored values; this does not recover the source Float32 ranking.
Historical Int8 `.vq` v1/v2 can still be structurally migrated to v3/tag 1
without requantisation. Merging historical Int8 decodes the stored values and
writes Float32 `.vec` v2. These compatibility paths do not create new Int8
encoding.

To continue indexing data from an old BBQ index, rebuild a separate Float32
index from the original documents and embeddings. BBQ decoding cannot recover
the original component magnitudes, so do not use decoded BBQ vectors as a
substitute for the source embeddings. See [CodecKit migrations](../contributors/codeckit/03-migrations.md)
and [ADR042](../articles/ADRs/ADR042-bbq-v3-osq-canonical-int8.md).

## Reindexing for preserved stop-word positions

LeanCorpus 4.0 and Rowles.Text 2.0 preserve position gaps when analysis removes
stop words. For example, `said the government` retains a gap between `said` and
`government`, so it no longer matches the exact phrase `"said government"`.
Query analysis follows the same rule: `"said the government"` uses positions
0 and 2.

Existing indexes must be rebuilt from the original text to receive corrected
positions. Removed positions cannot be recovered from postings, so codec
migration cannot repair them. Retain the original index or a backup, build a
separate index from source text with the corrected analyser, verify representative
phrase queries, then switch the application to that index.

Until reindexed, quoted phrases containing stop words can miss old segments
because query analysis now preserves gaps that those segments collapsed. Old
segments also retain false positives for phrases without stop words. A mixed
index can therefore return different phrase matches from old and new segments.
There is no per-segment query compatibility shim.

New flushes record `PositionContract` in `.seg` metadata. An absent marker means
legacy; the marker describes the writer's position contract, not the analyser
used. A merge keeps the marker only when every source is marked. Codec migration
carries the existing value and never stamps an old segment as corrected.

Index checks, format inspection, compatibility reports and migration plans emit
warning `LLIDX052` with `ReindexRecommended` guidance for unmarked segments. The
index **may be affected**, since it might not have used stop-word removal. This
warning does not change compatibility status, block an otherwise executable
migration, or prevent strict writers from appending to mixed indexes. Other
codec compatibility requirements still apply.

Inspection records observed legacy segments in
`leancorpus.index.position_contract.legacy_segments`, tagged with
`index.legacy_segment_count`; repeated inspections count repeated observations.
Term score explanations also include the warning for an unmarked segment,
without changing the score. See [ADR043](../articles/ADRs/ADR043-position-increments-survive-token-removal.md).

## Stemmers and analysis identities

LeanCorpus 4.0 and Rowles.Text 2.0 run the official Snowball 3.2.0 algorithms
for the English (Porter2), French, German, Spanish, Italian, Portuguese, Dutch,
Russian and Arabic stemmers. The 3.x stemmers differed from Snowball on between
2,251 (English) and 21,120 (Dutch) words of the published word lists, so many
stems change. `StemmedAnalyser` moves from the original Porter algorithm to
Porter2; `StemmerAnalyser.Porter()` and `PorterStemmerFilter` keep the original
algorithm. The German stop words also gained `ans`, `aufs`, `ins` and `vom`.
Fields analysed with any of these must be reindexed from source for queries to
match old and new documents consistently. Stemming for these languages is also
slower than in 3.x, which is the cost of matching Snowball exactly. See
[ADR050](../articles/ADRs/ADR050-snowball-stemmers-and-analysis-identities.md).

Each 4.0 commit records the analysis identity of every analysed field, and
`IndexWriter` refuses to analyse a field whose identity would change, with
`AnalysisIdentityMismatchException`. An index written before 4.0 has no
recorded identities, and codec migration carries them over unchanged, so a
migrated 3.1.1 index has none either. The writer therefore refuses to analyse
any field into it. Either:

- reindex from the original text into a new 4.0 index (recommended, and
  required anyway for corrected stems and stop-word positions); or
- set `IndexWriterConfig.AcceptAnalysisIdentityChanges = true` to record the
  current identities and keep writing. Existing terms are not reanalysed, so
  old and new documents keep their different stems and positions.

Check a query analyser against an index with
`IndexAnalysisIdentities.EnsureCompatible(directory, field, analyser)`. See
[Analysis identities and reindexing](../analysis/01-analysers.md#analysis-identities-and-reindexing)
and [ADR051](../articles/ADRs/ADR051-index-analysis-identity-enforcement.md).

## Score and result changes without reindexing

Some 4.0 query changes alter scores, rankings or matches on existing indexes
without any reindexing: phrase queries score by phrase frequency, prefix,
wildcard, regexp and fuzzy expansions score with global document frequency,
Boolean clause boosts apply on every path, span clauses score with their own
term frequencies, regexp queries match whole terms, and wildcard `?` matches
one Unicode character on every route. Compare relevance-sensitive results
after upgrading. See the [LeanCorpus 4.0.0 release notes](<../changelog/engine/4.0.0 - 2026-11-26.md>).
