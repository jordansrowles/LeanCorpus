# LeanCorpus DevOps release archive

This archive contains the single-file `devops` command for the runtime
identifier and build mode recorded in `distribution-manifest.json`.

Extract the archive, then run `devops --help` (Windows: `devops.exe --help`).
On Linux or macOS, extraction tools should preserve the executable bit; if the
file is not executable, run `chmod +x ./devops`.

Framework-dependent archives require the .NET 10 runtime. Self-contained
archives include the runtime used to launch the command. Both forms still
require Git for repository operations and the .NET SDK selected by the
checkout's `global.json` for build and project-evaluation commands. They do not
include repository-pinned tools, workloads or Native AOT toolchains. Run the
command from a compatible LeanCorpus checkout.

The checksum sidecar verifies the archive. `SHA256SUMS` inside the archive
verifies its executable, manifest, licence and notice files.
