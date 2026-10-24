# Managed DevOps publish compatibility

**State:** Linux-x64 bootstrap and managed service tests passed. Windows VM qualification is blocked because its checkout has no `.git` directory and Git is absent. Hosted Windows and macOS jobs have not run.

This record tracks the managed DevOps foundation's publish, SDK and MSBuild compatibility. A successful host build or cross-RID publish does not establish runtime support for that RID.

## Selected host and package graph

The managed host targets `net10.0`, independently of the SDK used to build or execute product operations. The current checkout selects SDK `11.0.100-preview.7.26381.103` under `global.json`; the selected MSBuild is `18.10.0.38203`. Installed .NET 10 and .NET 11 SDK/runtime versions are recorded in the [migration ledger](migration-ledger.json).

The dedicated DevOps package graph is pinned independently of product package metadata:

| Package | Version | Intended use | Runtime probe |
|---|---:|---|---|
| System.CommandLine | `3.0.0-preview.3.26207.106` | CLI parsing and help | Dedicated parser contracts pass |
| Bullseye | `6.2.0` | Explicit workflow dependencies | Scoped dependency, failure and cancellation tests pass |
| CliWrap | `3.10.5` | Child process execution | Real-child stream, timeout, cancellation and process-tree tests pass |
| Spectre.Console | `0.57.2` | Terminal output | Redirected output and command result tests pass |
| Microsoft.VisualStudio.SolutionPersistence | `1.0.52` | `.sln` and `.slnx` inspection | Managed `.slnx` project list matched `dotnet sln list` for the 37-project product solution |
| Microsoft.Build.Locator | `1.11.2` | Register selected SDK MSBuild | Registration attempted before evaluation; SDK 11 load incompatibility is recorded in PROBE-01 |
| Microsoft.Build | `17.14.28` | Evaluation only; exclude package runtime assets | Runtime load failed under the selected SDK; the narrow CLI fallback passed |
| `xunit.v3.mtp-v2` / Microsoft.Testing.Platform | `4.0.0` | Dedicated host tests | Dedicated MTP apphost passes 77/77 tests |

Do not add these packages to the product's central package graph. Do not use `BuildManager` as the build executor. Product restore/build/publish remain child `dotnet` operations.

## PROBE-01: .NET 10 host and SDK 11 MSBuild

**Question:** Can the `net10.0` DevOps process safely register and evaluate the selected SDK's MSBuild 18.10 using Locator 1.11.2 and the pinned Microsoft.Build 17.14.28 reference, with runtime assets excluded?

**Required evidence:**

1. Register Locator before any MSBuild type is loaded or JIT reaches an MSBuild-dependent method.
2. Evaluate a repository `.slnx` and representative project properties using the selected SDK.
3. Compare evaluated target path, TFM, configuration and project graph facts with the real SDK CLI output.
4. Confirm the pinned Microsoft.Build package does not bundle a competing MSBuild runtime in the DevOps publish output or dependency manifest.
5. Exercise the staged, single-file executable after publish, including MSBuild registration and evaluation.

**Fallback:** If PROBE-01 cannot load the selected SDK safely, use the specified narrow child command `dotnet msbuild <project> -getProperty:<property>` for properties and record structured results. Do not guess `TargetPath`, add a VSTest adapter, use BuildManager, change the product SDK, or weaken the `global.json` policy.

**Result:** The selected .NET 11 SDK/MSBuild 18 could not be loaded by the required `net10.0` managed host. The MSBuild API probe reports a `System.Runtime, Version=11.0.0.0` load failure while resolving SDK workload components. The implemented fallback runs the selected SDK CLI with `dotnet msbuild <project> -getProperty:Configuration,TargetFrameworks,TargetFramework,TargetPath`, then verifies the requested configuration and TFM and records stdout/stderr with the build run. The project inspector tests and representative managed builds passed using that metadata. SolutionPersistence enumeration matched `dotnet sln list`; no `BuildManager`, product SDK change or VSTest adapter was added. This is a tested fallback, not a successful MSBuild API load.

## Staged single-file checks

Linux setup ran twice and completed the locked restore, dedicated build, 77 pre-publish tests, single-file publish, staged `--help`/`--version` and build smoke, no-sidecar check, exclusive promotion, root fingerprint and promoted `--help`/`--version`. Direct root builds and PowerShell-forwarded builds passed. The host manifest is retained at `artifacts/devops/managed-host.json`. Checkout selection, SDK resolution, `.slnx` graph comparison, bounded child output/cancellation and stale/tampered host detection have managed tests. Native extraction behaviour is runtime-covered on Linux only. Promote only a validated executable. Keep staging and provenance below `artifacts/devops/`.

| RID | Required evidence | State |
|---|---|---|
| `linux-x64` | Fresh bootstrap twice, dedicated tests, single-file runtime probe, direct/forwarded build smoke and executable permissions | Passed |
| `win-x64` | Fresh bootstrap, dedicated tests, locked-file promotion and cancellation behaviour | Blocked: local guest checkout lacks `.git` and Git; hosted matrix not run |
| `osx-arm64` | Fresh bootstrap, single-file integration and command smoke | Not run |
| `linux-arm64` | Publish/load/native integration and representative command smoke | Not run |

Cross-publishing is not runtime evidence. Until each required host gate runs, that RID remains unqualified.

## Bootstrap compatibility

Keep `setup.ps1` as the checkout bootstrap and retain the existing pinned local DocFX tool restore. Every supported command uses the managed apphost. `devops.ps1` is a fingerprint-checking compatibility shim that forwards arguments and never imports the retired PowerShell command application. Bootstrap failures preserve the previous root executable and manifest, as covered by the temporary-checkout failure and rollback smoke.
