# LeanCorpus DevOps: unreleased

## Added

- Add managed backup/restore matrix qualification with Git source fingerprints, five legacy variants and linked child-run evidence.
- Add managed documentation preparation, metadata, build, static/browser validation and serve commands through the DevOps apphost.

## Changed

- Rename the affected-test, code-area and benchmark selectors from `Serialization` to `Serialisation`; the old selector values are no longer accepted.
- Record managed test environment override names in run manifests without persisting their values.
- Fail setup early with the exact missing DevOps project or configuration path, before restore, build or publish, and verify that the previous apphost is preserved. Keep the optional root `Directory.Build.targets` out of the required-input list and correct the bootstrap smoke's command capture and solution-path assertions.
- Import managed DevOps release notes as an independent changelog stream and derive its displayed project version separately from Core and Server versions.
- Bootstrap the managed host before collecting hosted coverage and call its coverage command directly.

## Fixed

- Prevent desktop docs navigation overflow by collapsing the navigation below 1600px and using stable Linux font metrics.
- Make the generated ADR relationship idempotence assertion recognise Windows line endings.
- Allow CI jobs to report bootstrap failures when no product-build diagnostics exist.
- Preserve managed coverage and documentation test run evidence when their CI jobs fail.
- Capture Chromium platform-font and viewport details with failed docs-site browser evidence.

## Removed

## Deprecated

## Security
- Reject docs deployments from pull-request Build runs and select the immutable, digest-verified site artefact from an exact successful run.
