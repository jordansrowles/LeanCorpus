# LeanCorpus 5.0.0 (unreleased)

## Added

- Browser WebAssembly support for volatile indexes on .NET 10 and .NET 11, with browser-specific buffer/cache defaults, serial indexing, flushes and merges, and async ingestion without a dedicated worker thread. ([1a0e52695](https://github.com/jordansrowles/LeanCorpus/commit/1a0e52695a3250aa79c6536ccd405a0894d37b55))
- Browser writers reject unsupported Brotli compression, durability and concurrency before acquiring ownership. Native durability requests, backup, restore, codec migration, parallel search and the background slow-query log report explicit capability errors before doing work. ([1a0e52695](https://github.com/jordansrowles/LeanCorpus/commit/1a0e52695a3250aa79c6536ccd405a0894d37b55), [f32c87da5](https://github.com/jordansrowles/LeanCorpus/commit/f32c87da52a3f02843e0b7364726582f68fb2823))
- Browser disposal retains resources and reports a retryable failure when active operations or child leases prevent synchronous close, instead of blocking the event loop. ([1a0e52695](https://github.com/jordansrowles/LeanCorpus/commit/1a0e52695a3250aa79c6536ccd405a0894d37b55))
- Exclusive file ownership, including `write.lock`, is enforced within a browser runtime, so a second `IndexWriter` on the same path fails with `WriteLockException`. ([1a0e52695](https://github.com/jordansrowles/LeanCorpus/commit/1a0e52695a3250aa79c6536ccd405a0894d37b55), [f32c87da5](https://github.com/jordansrowles/LeanCorpus/commit/f32c87da52a3f02843e0b7364726582f68fb2823))

## Compatibility

- Desktop defaults, public constructors and index formats are unchanged. Browser commits remain volatile and do not persist across page reloads. No reindex is required. ([1a0e52695](https://github.com/jordansrowles/LeanCorpus/commit/1a0e52695a3250aa79c6536ccd405a0894d37b55))
- Package versions remain at the released baseline during this feature's review. Select the 5.0 package versions and dependent compression-package ranges in the release preparation; these source changes must not be published over the released 4.0 packages. ([1a0e52695](https://github.com/jordansrowles/LeanCorpus/commit/1a0e52695a3250aa79c6536ccd405a0894d37b55))
