# Managed DevOps lab runner

**Status:** implementation specification, not implemented.  
**Priority:** small follow-on to the managed DevOps foundations; implement before the GC workload work.  
**Reference checkout:** `jordansrowles/LeanCorpus:issue-97` at `197f3c091354ed246d6e75b4287d62fa2ed9c7a6` (design snapshot, **not** an implementation pin).  
**First consumer:** the stock-GC Step 0 study in `jordansrowles/notes`.

## Purpose and boundary

Add `./devops lab` as a modest, reusable way to run predeclared performance investigations without an agent supervising a VM. A **lab definition** describes a published .NET workload, selected scenarios, runtime configuration profiles and a bounded ordered run sequence. A **playlist** references one or more lab definitions and decides whether failure of each is required or optional.

The runner prepares once, executes **one fresh child process at a time**, records evidence after every child, and writes a mechanically collated review. It does not interpret GC research, implement an Elsa-style workflow engine, replace BenchmarkDotNet, or schedule the VM. GC is the first workload, not a dependency of the generic runner.

This specification **supersedes the orchestration, CLI and common artefact portions** of the older `02-Stock-GC/Harness-Architecture.md`. That document remains the GC-specific workload, instrumentation and evidence contract.

## Current code to build on

| Existing code | Reuse / change |
| --- | --- |
| `Rowles.LeanCorpus.DevOps/Program.cs`, command handler and options | Add `lab validate` and `lab run`; honour recursive `--repo` and existing result/exit conventions |
| `Infrastructure/ProcessRunner`, `ProcessRequest` | Reuse CliWrap, stdout/stderr capture, timeout and cancellation. Add **per-child environment set/remove** without changing other callers |
| `Artifacts/ArtifactPaths`, `ArtifactRunStore`, `RunManifest` | Add category `lab`; one existing batch manifest, small per-child result records. Do not turn `RunManifest` into a research schema |
| `RepositoryPaths`, `GitRepositoryProvenanceReader` | Resolve selected checkout and record exact source revision/dirty state |
| `BuildService` and `IProcessRunner` | Reuse existing process/build conventions; a small publish step is sufficient if build service does not publish |
| `WorkflowRunner` (Bullseye) | Not used for each lab run; the lab's ordered serial loop is simpler |
| `Rowles.LeanCorpus.DevOps.ProcessTestChild` | Test exit codes, logs, timeout, cancellation, environment isolation and run evidence |

The managed migration is still in progress. Re-check the branch before editing; avoid restructuring foundations being changed by the other agent.

## CLI contract (v1)

```text
./devops --repo /path/to/leancorpus lab validate /path/to/playlist.json
./devops --repo /path/to/leancorpus lab run /path/to/playlist.json --dry-run
./devops --repo /path/to/leancorpus lab run /path/to/playlist.json
```

- `validate`: parse both schemas, resolve all references/profiles, check duplicates, unsupported features, paths, revisions and required inputs; do not publish, launch or create measurement evidence.
- `run --dry-run`: perform validation and print the resolved, numbered serial plan, profile overlays, preparation target, estimated run count and intended artefact root; **no build or measured child**.
- `run`: validate **everything first**, publish each distinct workload once, then execute. Preparation failure stops the playlist before measurement. Normal child failures are recorded and the next scheduled run continues unless cancellation occurs.
- An optional lab's failure is visible but need not fail the overall playlist; a required lab's failure makes the final exit non-zero. `onFailure: "continue"` never converts a failed required lab into success.
- Do not add `lab report`, `lab resume`, arbitrary script steps, parallel workers or a scheduler in v1. The report is written automatically; a later explicit resume feature must validate the plan and evidence hashes before skipping runs.

## File contract (version 1)

All paths in playlist/definition files are resolved relative to **the file containing the path**, never to the caller's current directory. Reject unknown schema versions, unknown fields, duplicate IDs, cycles in profile inheritance, invalid timeouts, empty sequences, non-finite numeric settings, unsupported diagnostics and unresolved inputs. A playlist may reference multiple definitions but not other playlists.

**Playlist example** (`gc-step0.playlist.json`):

```json
{
  "schemaVersion": 1,
  "name": "gc-step0-aa",
  "onFailure": "continue",
  "labs": [
    { "definition": "./gc-step0-aa.lab.json", "required": true }
  ]
}
```

**Lab definition example** (the concrete initial file lives in the notes repository):

```json
{
  "schemaVersion": 1,
  "name": "gc-step0-aa",
  "workload": {
    "project": "src/devops/benchmarks/Rowles.LeanCorpus.Lab.Workload/Rowles.LeanCorpus.Lab.Workload.csproj",
    "framework": "net10.0",
    "entrypoint": "Rowles.LeanCorpus.Lab.Workload.dll"
  },
  "profileCatalogue": "./leancorpus-gc-profiles.json",
  "scenarios": [
    { "id": "W00", "arguments": [], "timeoutSeconds": 120 }
  ],
  "repeatBlocks": 3,
  "sequence": [
    { "scenario": "W00", "profile": "B00", "diagnostics": "D0", "label": "A1" },
    { "scenario": "W00", "profile": "B00", "diagnostics": "D0", "label": "A2" }
  ]
}
```

- `workload.project` is relative to the selected LeanCorpus checkout (`--repo`), not to the notes file. The project and entrypoint must be inside that checkout's expected published output. v1 deliberately supports **one checkout per invocation**; run a separate invocation for another repository on the VM. No cloning, fetching or branch switching.
- A `sourceRevision` pin may be supplied on the lab definition for unattended VM use; when present, reject a mismatching HEAD. Always record HEAD and dirty status; **dirty or unpinned runs are exploratory, not comparison-grade**. The VM operator pins its checkout before launching.
- `profileCatalogue` consumes the existing GC profile file's `profiles` array: `id`, optional `baseProfile`, `configProperties`, and metadata. Resolve overlays without mutating the catalogue. Generic DevOps only understands runtimeconfig properties, not GC tuning conclusions.
- `repeatBlocks` repeats the **entire ordered sequence**, preserving deliberate A/A or ABBA ordering. Do not generate a profile × scenario Cartesian product. `label` distinguishes otherwise identical control runs.
- Only `D0` (unprofiled) is executable in v1. Reject D1/D2/D3 clearly until separately implemented; never silently downgrade a requested diagnostic mode.
- A profile containing `managedAction` is unsupported in v1, including L01. Reject `guardedStress` profiles. Heap hard-limit and other memory-constrained cases must not execute without a verified cgroup/safety capability, which is **out of v1**. The initial playlist uses only safe baseline profiles. The runner should reject known `System.GC.HeapHardLimit*` settings until this capability exists.
- Scenario arguments are a string array, passed without a shell. No arbitrary `prepare` or `run` commands in JSON. The workload contract supplies the executable and accepts `--scenario <id> --output <absolute-run-dir>` followed by scenario arguments. Fixtures, if used, are declared as paths in scenario arguments and validated by the GC host.

## Preparation and process contract

1. Resolve checkout, validate source and all JSON, fingerprint playlist/definition/catalogue and record the complete expanded plan.
2. Publish the framework-dependent .NET workload once per unique project/framework/configuration into a batch-owned directory. Use a fixed SDK/runtime and the existing DevOps process conventions. Never use `dotnet run`, restore, build or publish inside a measured child.
3. Preserve the published runtimeconfig and deps file. For each child, create a **private runtimeconfig overlay** that merges `runtimeOptions.configProperties` from the published runtimeconfig with the resolved profile. Preserve all unrelated runtime options and dependency metadata. Do not rewrite the shared published runtimeconfig.
4. Launch the published DLL with `dotnet exec --runtimeconfig <private-file> --depsfile <published-depsfile> <published-dll> --scenario ... --output ...`. Resolve the actual dotnet host explicitly. Check that the selected framework/runtime is available and record its exact version. Do not silently roll forward to a different major runtime.
5. Strip inherited GC-related `DOTNET_GC*` and `COMPlus_GC*` environment variables from the **child only**; capture names of removed keys without recording secret values. Starting with .NET 9, environment settings can override runtimeconfig. A .NET 11 `*.runtimeconfig.dev.json` can also override ordinary runtimeconfig; reject unexpected development overrides or verify that the effective profile is unaffected.
6. Capture complete stdout/stderr, wall time, child exit status, output checksum and result JSON. Do not mix host/publish timings with measured workload timings.
7. On cancellation or timeout, ensure the child/process tree is terminated, retain partial evidence, mark an explicit terminal state and do not leave a second measured process running. Verify the current CliWrap behaviour with integration tests rather than assuming process-tree cleanup.
8. No parallel measured children. Host quietness, cold/warm cache policy and OS/cgroup setup remain explicit scenario/VM prerequisites, not generic orchestration features.

Use `ProcessRequest`'s existing timeout/log handling and add a minimal environment overlay/removal API; don't create a second process supervisor. Run-private runtimeconfig is compatible with `dotnet exec --runtimeconfig`; a full copy of the published output per repetition is unnecessary unless a workload proves it needs one.

## Workload result contract

The executable writes `results.json` atomically inside its assigned run directory, with:

```json
{
  "schemaVersion": 1,
  "scenario": "W00",
  "workChecksum": "sha256:<semantic-output-digest>",
  "metrics": {
    "operations": 1000,
    "elapsedMilliseconds": 1000.0
  },
  "runtime": {
    "frameworkDescription": ".NET ...",
    "isServerGC": true,
    "gcConfiguration": {}
  }
}
```

Values above illustrate schema, **not observed results**. The GC workload host is responsible for actual timings, checksums, phase markers and runtime attestations. Compare `GC.GetConfigurationVariables()` with the requested profile **where observable**, allowing documented clamps such as heap count; also record `GCSettings.IsServerGC`, `GC.GetGCMemoryInfo()`, CPU count and runtime identity. Unverifiable required settings yield `configuration-unverified`, not an assumed pass. Generic lab code stores the attestation; it does not implement a GC-specific interpretation engine.

A successful child exit is **not** enough: missing/invalid result JSON, incorrect scenario, failed work checksum, missing mandatory attestation or incompatible runtime make that run invalid. Same-scenario semantic work checksums must agree across profile variants; never use nondeterministic raw segment bytes as a substitute for equivalent work.

## Artefacts and terminal states

Use `ArtifactRunStore(paths, runCategory: "lab")` and add `lab` to `ArtifactPaths.GetRunRoot`. Store under:

```text
artifacts/lab/runs/<batch-guid>/
  manifest.json                  # existing DevOps batch RunManifest
  plan.resolved.json             # playlist, definitions, profiles, order and input hashes
  preparation/                   # publish logs and published workload
  runs/0001/                     # one directory per child, ordered
    runtimeconfig.json
    profile.resolved.json
    result.json                  # runner validation / state
    results.json                 # workload metrics / attestation
    stdout.log
    stderr.log
    phases.ndjson                # optional, workload-owned
  review/summary.json
  review/SUMMARY.md
  review/runs.csv
```

Each child result is written atomically **immediately after that child ends**. Keep separate dimensions for execution (`succeeded/failed/timed-out/cancelled/interrupted`), configuration (`verified/unverified/mismatch`) and measurement (`valid/invalid/not-assessed`). A batch that was interrupted retains completed child evidence; **v1 does not automatically resume**. The review collates actual and expected runs, failed/skipped counts, scenario/profile/diagnostic grouping, semantic checksum agreement and simple descriptive metrics. No statistical significance or GC-causality claims.

Protect lab runs from `clean default`. An explicit `clean lab` may be added with existing lease/active-run protection, but do not silently erase historical evidence or fixtures.

## Implementation order and tests

1. CLI options and strict JSON models/resolution/validation; test bad paths, cycles, duplicate IDs, unsupported profiles and dry-run's lack of side effects.
2. Small `ProcessRequest` environment set/remove extension, tested against `DevOps.ProcessTestChild`, including inheritance and cancellation.
3. `lab` artefact category, batch manifest and per-child atomic result writer.
4. Publish-once and private runtimeconfig generation; test that base runtime options survive and published output remains unchanged, including .NET 11 dev override and environment precedence cases.
5. Serial `LabService` with ordered block expansion, required/optional failure semantics, cancellation and final mechanical review.
6. Smoke with a tiny W00 workload. Then implement GC scenario host W02/W03/W10 under the **separate GC Step 0 spec**. Do not implement GC attribution, trace parsing or cgroup machinery as part of this ticket.

**Done when:** a valid playlist can run repeated W00 controls unattended on Linux, each in a fresh process with verified runtime settings and identical semantic checksums; a failing child does not erase evidence or become success; `--dry-run` launches nothing; the existing build, clean, benchmark and server commands retain their behaviour.

## Explicitly deferred

D1–D3 diagnostics; resume/retry; process parallelism; remote execution; multi-repo orchestration in one invocation; automatic Linux cgroup setup; automatic VM scheduling; benchmark statistics; adaptive GC control; custom collectors; standalone package extraction; a workflow designer or UI.

## Source notes

- Current managed foundations: `src/devops/tooling/Rowles.LeanCorpus.DevOps/{Program.cs,Infrastructure/ProcessRunner.cs,Artifacts/ArtifactRunStore.cs,Workflows/WorkflowRunner.cs}` on `issue-97`.
- [Microsoft: runtime configuration and precedence](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/) (including .NET 11 development overrides).
- [Microsoft: dotnet exec --runtimeconfig and --depsfile](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet).
- [Microsoft: ProcessStartInfo.Environment](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.environment).
- [Microsoft: GC.GetConfigurationVariables](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getconfigurationvariables).
- [Microsoft: dotnet-trace profiles](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace) for deferred D1/D2 work.
