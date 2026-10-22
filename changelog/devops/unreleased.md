# LeanCorpus DevOps: unreleased

## Added

- Add managed backup/restore matrix qualification with Git source fingerprints, five legacy variants and linked child-run evidence.
- Add managed documentation preparation, metadata, build, static/browser validation and serve commands through the DevOps apphost.

## Changed

- Record managed test environment override names in run manifests without persisting their values.
- Fail setup early with the exact missing DevOps project or configuration path, before restore, build or publish, and verify that the previous apphost is preserved. Keep the optional root `Directory.Build.targets` out of the required-input list and correct the bootstrap smoke's command capture and solution-path assertions.
- Import managed DevOps release notes as an independent changelog stream and derive its displayed project version separately from Core and Server versions.
- Bootstrap the managed host before collecting hosted coverage and call its coverage command directly.

## Fixed

- Make the generated ADR relationship idempotence assertion recognise Windows line endings.
- Allow CI jobs to report bootstrap failures when no product-build diagnostics exist.

## Removed

## Deprecated

## Security
