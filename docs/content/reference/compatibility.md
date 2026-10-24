# Compatibility

Use this page before upgrading packages or moving an index between deployments.

| Boundary | Rule |
|---|---|
| Runtime | LeanCorpus and Rowles.Text target `net10.0` and `net11.0`. |
| Packages | Keep LeanCorpus and optional compression packages on the same released version. |
| Indexes | 4.x reads and migrates supported 3.x indexes. Opening 4.x output with a 3.x reader is unsupported; retain a pre-upgrade backup. |
| Codecs | Codec changes require a migration or explicit backward-read compatibility. |
| Spatial point kinds | Geo and XY point encodings are distinct despite sharing the same Packed BKD dimensions. Queries resolve kind per segment; metadata-less legacy Geo requires both `_lat` and `_lon` fields. Incompatible kinds cannot be merged under one field name. No codec change or migration is required. |
| Shape DocValues | LeanCorpus 4.0 adds `.dvg` v1 under the existing DocValues family. It does not change `.pbkd` v1 or require migration. Indexed shape queries continue to use Packed BKD when `.dvg` is absent; shape centroid and bounds aggregations require complete `.dvg` coverage for the field. |
| Native AOT | Core contracts are supported; optional codec registration must be explicit. |

Use [Upgrade guide](upgrades.md) and [CodecKit migrations](../contributors/codeckit/03-migrations.md) for release-specific actions.
See [Shape DocValues and spatial aggregations](../articles/ADRs/ADR035-shape-docvalues-and-spatial-aggregations.md) for the persisted format contract.
