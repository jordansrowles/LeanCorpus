# LeanCorpus DevOps: unreleased

## Added

- Add managed backup/restore matrix qualification with Git source fingerprints, five legacy variants and linked child-run evidence.

## Changed

- Record managed test environment override names in run manifests without persisting their values.
- Fail setup early with the exact missing DevOps project or configuration path, before restore, build or publish, and verify that the previous apphost is preserved. Keep the optional root `Directory.Build.targets` out of the required-input list and correct the bootstrap smoke's command capture and solution-path assertions.
- Import managed DevOps release notes as an independent changelog stream and derive its displayed project version separately from Core and Server versions.

## Fixed

## Removed

## Deprecated

## Security
