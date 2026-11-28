# LeanCorpus DevOps: unreleased

## Added

- Explicit `browser` tests publish a trimmed Blazor client and validate Core in Chromium and Firefox on .NET 10 and .NET 11, in interpreted and WASM AOT modes. Probes load prebuilt index payloads and record memory, blocking and capability limits. The suite has separate CI jobs that build the client before running, affected-file routing and external timeouts, and stays out of generic `test all` selection. ([1a0e52695](https://github.com/jordansrowles/LeanCorpus/commit/1a0e52695a3250aa79c6536ccd405a0894d37b55), [f32c87da5](https://github.com/jordansrowles/LeanCorpus/commit/f32c87da52a3f02843e0b7364726582f68fb2823))

## Fixed

- `./devops docs test` accepts a changelog stream whose only release page is a dated version file, so a release that renames `unreleased.md` no longer fails static validation with "Required staged document is missing". ([fcfce5bfa](https://github.com/jordansrowles/LeanCorpus/commit/fcfce5bfa306e8b8eee90267eda53b23fd6dabf0))
