# Compatibility

Use this page before upgrading packages or moving an index between deployments.

| Boundary | Rule |
|---|---|
| Runtime | LeanCorpus and Rowles.Text target `net10.0` and `net11.0`. |
| Packages | Keep LeanCorpus and optional compression packages on the same released version. |
| Indexes | Check release upgrade notes before opening an index written by an earlier version. |
| Codecs | Codec changes require a migration or explicit backward-read compatibility. |
| Shape DocValues | LeanCorpus 3.2 adds `.dvg` v1 under the existing DocValues family. It does not change `.pbkd` v1 or require migration. Indexed shape queries continue to use Packed BKD when `.dvg` is absent; shape centroid and bounds aggregations require complete `.dvg` coverage for the field. |
| Native AOT | Core contracts are supported; optional codec registration must be explicit. |

Use [Upgrade guide](upgrades.md) and [CodecKit migrations](../contributors/codeckit/03-migrations.md) for release-specific actions.
See [Shape DocValues and spatial aggregations](../articles/ADRs/ADR035-shape-docvalues-and-spatial-aggregations.md) for the persisted format contract.
