# Historical physical index fixtures

This tree generates historical physical index snapshots using a published
LeanCorpus NuGet package. It is excluded from the normal solution build.
`fixtures.json` pins each supported package archive and
assembly, version project, scenario, output manifest and committed archive.

List available scenarios with:

```bash
./devops data fixtures --list
```

Generate a scenario into a new output directory:

```bash
./devops data fixtures --version 3.1.1 --scenario sparse-hnsw
./devops data fixtures --version 3.1.1 --scenario migration
```

Use `--package-source` to supply a local package directory or an HTTPS NuGet
feed. Restore uses only that source, an isolated package cache, the checked-in
lock file, and the exact versioned project. The generator verifies both the
`.nupkg` SHA-256 and loaded assembly SHA-256 before invoking a scenario. It does
not reference the current Core project or its writers.

Each output directory must be new. If `--output-dir` is omitted, generated
archives and manifests are written beneath the command's run evidence in
`artifacts/data/runs/<run-id>/generated/`. Compare them with the committed
snapshots at `src/devops/tests/Rowles.LeanCorpus.Tests.Shared/Fixtures/Indexes/`;
ordinary tests read only those immutable snapshots.

The `sparse-hnsw` scenario preserves Float32 and Int8 vectors in loose and
compound indexes, with two segments per layout and sparse graph membership.
The `migration` scenario preserves the comprehensive release corpus in loose
and compound layouts, including stored fields, deletions, vector and Geo
compatibility data.

`data migration-fixtures-311` remains as a deprecated alias for the migration
scenario while automation moves to `data fixtures --version 3.1.1 --scenario
migration`.
