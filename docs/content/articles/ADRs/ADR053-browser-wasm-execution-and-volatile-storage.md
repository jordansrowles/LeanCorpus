---
adr: ADR053
title: "Browser WASM uses serial execution and volatile storage"
date: 2026-11-26
status: Accepted
version-added: v5.0.0
summary: "Adapt execution at engine boundaries without creating a second index format or promising browser persistence."
areas: [indexing, search, store, concurrency]
depends-on: [ADR021, ADR027, ADR029, ADR030, ADR032, ADR041]
---

# ADR053: Browser WASM uses serial execution and volatile storage

- **Date:** 2026-11-26
- **Status:** Accepted

## Context

Blazor WebAssembly applications need client-side indexing and search without a server round trip. The existing segment codecs and memory-mapped reader work with the .NET browser runtime's virtual filesystem, so the index formats themselves are not the obstacle.

The desktop execution model is. It includes a dedicated asynchronous writer consumer, thread-pool flushes and merges, parallel segment search, synchronous lifecycle drains and native durability calls. The supported browser runtime has one thread, shared with the page's event loop. Work queued to the thread pool runs on that same thread, a synchronous wait for it can never complete, and native durability calls do not exist. Merely setting `DurableCommits = false` leaves asynchronous ingestion capable of hanging the renderer.

The browser filesystem is held in WebAssembly memory and is discarded on page reload. It accepts every advisory lock, so file sharing modes give no exclusion.

## Decision

Use `OperatingSystem.IsBrowser()` at existing execution and filesystem seams. Do not add a second directory abstraction, codec implementation or index format. `MMapDirectory` addresses volatile runtime files in a browser; neither its name nor a successful commit implies durable browser persistence.

Browser producers, physical flushes, merges and segment search run serially. Physical flush results retain the existing ordered coordinator publication and failure ownership. Automatic merge output is adopted into writer state; commit publication still requires the normal commit path. No browser operation depends on `Task.Run` running inline or on a dedicated thread.

Async write APIs process accepted commands inline in a browser. They retain validation, chunking, block adjacency, partial-success, cancellation-before-admission and writer-owned completion semantics. Streaming ingestion can suspend while consuming its source. `CommitAsync` performs commit work inline. These signatures do not promise UI offloading or asynchronous storage I/O.

Select conservative browser buffering and cache defaults. They are configurable thresholds, not a heap ceiling. Caller documents, virtual files, mappings, active reader states, vectors and merge output remain additional allocations. Leave desktop defaults and execution unchanged.

Reject Brotli stored-field writes (unsupported by the interpreted browser runtime), native durability, producer concurrency above one, flush or merge concurrency other than one, parallel segment search and the background slow-query logger with explicit capability errors. Writer construction validates before acquiring `write.lock`; later operation admission and commit preparation and publication recheck mutable configuration. Browser filesystem barriers throw instead of attempting `libc` or silently succeeding. Search never enables parallel segment work after configuration mutation.

`FileShare.None` opens made through the Store facade use an in-process exclusive-ownership registry in a browser. This keeps `write.lock`, the migration lock and spill files exclusive within one runtime; it does not provide cross-tab or persistent locking. Backup, restore and codec migration depend on durable barriers and are rejected with `PlatformNotSupportedException` before they write output.

Synchronous close must not wait for an incomplete browser operation or retained child mapping. Close admission and retain ownership, throw a clear `InvalidOperationException`, then allow disposal to be retried after operations finish and child leases are released. A composed runtime uses asynchronous drains directly; its synchronous adapter rejects an incomplete close instead of blocking the event loop. Desktop waiting and retry contracts remain unchanged.

## Rationale

- **One engine and one format.** The browser runtime's virtual filesystem supports the path, mapping and metadata operations the codecs already use. A dictionary-backed browser directory would require changing path-oriented codecs and metadata operations without evidence that this is necessary. A separate browser search implementation would duplicate query semantics and every compatibility contract. Both were rejected. Keeping the formats also makes desktop-built indexes usable as browser payloads, and the reverse.
- **Serial inline execution.** On a single thread, handing work to the thread pool only defers it, and a dedicated consumer or a synchronous wait stalls the page. Running the existing operations inline at the existing seams keeps their ownership, ordering and failure rules, instead of re-implementing them for the browser.
- **Explicit rejection over silent degradation.** ADR029 forbids reporting a barrier that did not happen as durable. A no-op barrier, a silently serialised parallel option or a background logger that never runs would each misreport behaviour. Capability errors before ownership is acquired leave no half-built writer.
- **Retryable close.** Blocking the event loop until an operation finishes would deadlock, because the operation can only finish on the same thread. Rejecting the close and retaining resources preserves the ADR027 and ADR041 lifetime guarantees.
- **Volatile storage first.** IndexedDB and OPFS persistence need asynchronous storage I/O, which ADR032 does not provide, plus cross-tab ownership. They are a separate storage decision. A volatile contract is useful on its own for indexes built in the page or shipped as prebuilt payloads.

## Consequences

- Existing public constructors, namespaces and persisted formats are retained. No reindex is required by this change. Rowles.Text source and optional codec implementations are unchanged.
- The supported execution model is single-threaded .NET 10 and .NET 11 `browser-wasm`. Shared-memory or threaded browser builds, WASI and dedicated worker hosting require separate qualification.
- IndexedDB, OPFS, cross-tab ownership, persistent browser locking and public archive import or export are outside this storage contract. Trusted test archives establish desktop and browser format interchange only.
- CPU work runs on the page's thread. Large indexing batches and merges block the UI until they finish; moving work off the UI thread requires worker hosting.
- Trimmed interpreted publishing and WASM AOT are separate gates. AOT requires a matching workload and a real published-browser run. No performance or mobile memory guarantee follows from correctness tests.
- Browser mappings are copies of virtual files in a 2 GiB WebAssembly heap that does not shrink. Searcher open validates committed segments, copying each compound file; each searcher holds its own copies. Prebuilt payloads are application assets loaded into the virtual filesystem and opened read-only. Measured multiples are recorded in the browser guide, not in this decision.
- Default Chinese and Japanese analysers locate lexicons on disk and are not qualified in a browser. Lexicon availability is not part of an analysis identity; fixing that is a Rowles.Text and analysis-identity decision outside this ADR.
- Built-in None and Deflate stored fields are supported. Brotli stays outside the browser support contract because the interpreted runtime rejects it; write admission uses the same capability policy in interpreted and AOT deployments. Transcode imported Brotli fields on desktop first. Optional packages with native dependencies need their own browser evidence.
- The explicit managed `browser` suite publishes a maintained Blazor client and drives it with Playwright under external timeouts. It covers defaults, invalid configuration, async ingestion, cancellation, backpressure, retained ownership, merges, reopen, searcher refresh, queries and bidirectional desktop and browser fixtures, in Chromium and Firefox on both target frameworks. Desktop Core, architecture, package consumers, Native AOT and documentation gates protect the existing boundaries. Run results belong in the browser guide and run evidence, not in this decision.
- Browser tests are non-packable. The browser client receives internal test visibility solely to verify actual mapping and runtime ownership; the package gains no public testing API.
- Follow-up feasibility work is tracked in issue #138: memory amplification from mapping copies, out-of-memory reported as index corruption, Chinese and Japanese lexicon availability, Brotli stored-field reads and UI blocking. A fix that changes storage, analysis identity or execution hosting amends or supersedes this decision.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-11-26 | Accepted | Serial browser execution, volatile storage, in-runtime exclusive opens and up-front rejection of durability-dependent operations. | `#138` |
