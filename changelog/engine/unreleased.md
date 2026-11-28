# LeanCorpus 5.0.0 (unreleased)

## Added

- Browser WebAssembly support for volatile indexes on .NET 10 and .NET 11, with browser-specific buffer/cache defaults, serial indexing, flushes and merges, and async ingestion without a dedicated worker thread. ([d4e06a97d](https://github.com/jordansrowles/LeanCorpus/commit/d4e06a97d0c41c5cc76143d464e712ba2dabc5c0))
- Browser writers reject unsupported Brotli compression, durability and concurrency before acquiring ownership. Native durability requests, backup, restore, codec migration, parallel search and the background slow-query log report explicit capability errors before doing work. ([d4e06a97d](https://github.com/jordansrowles/LeanCorpus/commit/d4e06a97d0c41c5cc76143d464e712ba2dabc5c0))
- Browser disposal retains resources and reports a retryable failure when active operations or child leases prevent synchronous close, instead of blocking the event loop. ([d4e06a97d](https://github.com/jordansrowles/LeanCorpus/commit/d4e06a97d0c41c5cc76143d464e712ba2dabc5c0))
- Exclusive file ownership, including `write.lock`, is enforced within a browser runtime, so a second `IndexWriter` on the same path fails with `WriteLockException`. ([d4e06a97d](https://github.com/jordansrowles/LeanCorpus/commit/d4e06a97d0c41c5cc76143d464e712ba2dabc5c0))

## Compatibility

- Desktop defaults, public constructors and index formats are unchanged. Browser commits remain volatile and do not persist across page reloads. No reindex is required. ([d4e06a97d](https://github.com/jordansrowles/LeanCorpus/commit/d4e06a97d0c41c5cc76143d464e712ba2dabc5c0))
- Package versions remain at the released baseline during this feature's review. Select the 5.0 package versions and dependent compression-package ranges in the release preparation; these source changes must not be published over the released 4.0 packages. ([d4e06a97d](https://github.com/jordansrowles/LeanCorpus/commit/d4e06a97d0c41c5cc76143d464e712ba2dabc5c0))
