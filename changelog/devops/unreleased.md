# LeanCorpus DevOps: unreleased

## Added

- Explicit `browser` tests publish a trimmed Blazor client and validate Core in Chromium and Firefox on .NET 10 and .NET 11, in interpreted and WASM AOT modes. Probes load prebuilt index payloads and record memory, blocking and capability limits. The suite has separate CI jobs that build the client before running, affected-file routing and external timeouts, and stays out of generic `test all` selection. ([d4e06a97d](https://github.com/jordansrowles/LeanCorpus/commit/d4e06a97d0c41c5cc76143d464e712ba2dabc5c0))

## Fixed

- `./devops docs test` accepts a changelog stream whose only release page is a dated version file, so a release that renames `unreleased.md` no longer fails static validation with "Required staged document is missing". ([fcfce5bfa](https://github.com/jordansrowles/LeanCorpus/commit/fcfce5bfa306e8b8eee90267eda53b23fd6dabf0))
