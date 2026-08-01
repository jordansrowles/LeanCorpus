# Compatibility

Use this page before upgrading packages or moving an index between deployments.

| Boundary | Rule |
|---|---|
| Runtime | LeanCorpus and Rowles.Text target `net10.0` and `net11.0`. |
| Packages | Keep LeanCorpus and optional compression packages on the same released version. |
| Indexes | Check release upgrade notes before opening an index written by an earlier version. |
| Codecs | Codec changes require a migration or explicit backward-read compatibility. |
| Native AOT | Core contracts are supported; optional codec registration must be explicit. |

Use [Upgrade guide](upgrades.md) and [CodecKit migrations](../contributors/codeckit/03-migrations.md) for release-specific actions.
