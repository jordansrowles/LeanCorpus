# Upgrade guide

Read the release changelog before upgrading a deployed index.

1. Check the target package and runtime versions in [Compatibility](compatibility.md).
2. Read the release entry for removed APIs, index-format changes and codec migrations.
3. Back up the index or retain a known-good commit before migration.
4. Test opening, searching and writing a copy of production data.
5. Deploy application and package changes together, then monitor recovery and merge diagnostics.

Use [CodecKit migrations](../contributors/codeckit/03-migrations.md) for binary-format work and [Changelog](../changelog/index.md) for release notes.
