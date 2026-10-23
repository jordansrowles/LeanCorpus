# LeanCorpus DevOps: unreleased

## Added

- Add managed `test` and `aot` apphost commands backed by the shared test planner and executor; keep the PowerShell dispatcher and CI routes in place pending the complete Phase 5 parity gate.
- Run the Build workflow on `issue-97` pushes so the migration branch receives exact-source CI qualification.
- Establish DevOps `1.0.0` independently of the LeanCorpus Core package version.
- Add managed backup/restore matrix qualification with Git source fingerprints, five legacy variants and linked child-run evidence.
- Add managed documentation preparation, metadata, build, static/browser validation and serve commands through the DevOps apphost.
- Add a framework-dependent .NET 10 tool distribution profile for the managed DevOps command, with package readme and licence metadata.
- Add managed RID-specific single-file DevOps release archives with source manifests, third-party notices, payload checksums and archive SHA-256 sidecars.
- Add an interactive Spectre.Console workbench for managed tests, benchmark suites and area/group selectors, DataForge qualification and recent run inspection.
- Exercise managed DevOps bootstrap and `report all` on Linux ARM64 and macOS ARM64 in Build CI.

## Changed

- Include all tracked C# projects in the managed code dependency graph, report solution and tracked project counts separately, and retain SDK-evaluated package metadata.
- Run CI product builds and the manual benchmark command through the checked-out managed DevOps apphost after setup.
- Bootstrap the checked-out managed DevOps host before package validation and consumer checks in Build CI.
- Upload linked MTP test-run artefacts separately from coverage-run manifests in Build CI.
- Use British spelling for Core serialisation source paths, namespaces, test selectors, affected-source selectors and benchmark area/group selectors.
- Keep repository-host, .NET tool and release-archive dependency graphs isolated with profile-specific NuGet lock files.
- Record managed test environment override names in run manifests without persisting their values.
- Fail setup early with the exact missing DevOps project or configuration path, before restore, build or publish, and verify that the previous apphost is preserved. Keep the optional root `Directory.Build.targets` out of the required-input list and correct the bootstrap smoke's command capture and solution-path assertions.
- Import managed DevOps release notes as an independent changelog stream and derive its displayed project version separately from Core and Server versions.
- Bootstrap the managed host before collecting hosted coverage and call its coverage command directly.
- Run `report all --json` through promoted Linux and Windows hosts in Build CI and retain the full report run evidence.

## Fixed

- Exclude ignored build output from repository report dirty-state and status entries while retaining untracked source changes.
- Correct the Lucene.NET Geo distance benchmark radius units, disable its default spatial query tolerance for fixture parity, and use unique fuzzy no-hit terms to avoid cached-empty-result bias.
- Disable build servers for benchmark child runs and report the benchmark target, lease stage, elapsed time, and latest child output in progress updates.
- Prevent desktop docs navigation overflow by collapsing the navigation below 1600px and using stable Linux font metrics.
- Make the generated ADR relationship idempotence assertion recognise Windows line endings.
- Allow CI jobs to report bootstrap failures when no product-build diagnostics exist.
- Preserve managed coverage and documentation test run evidence when their CI jobs fail.
- Capture Chromium platform-font and viewport details with failed docs-site browser evidence.
- Give interactive workbench users a documented-command fallback when their terminal does not support ANSI selections.
- Handle Ctrl+C during interactive prompts as a graceful cancellation and restore terminal state.

## Removed

## Deprecated

## Security
- Reject docs deployments from pull-request Build runs and select the immutable, digest-verified site artefact from an exact successful run.
