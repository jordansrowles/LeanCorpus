# ADR: ConcurrentDictionary with generation-swap eviction for read-heavy caches

- **Date:** 2026-06-16
- **Status:** Accepted

## Context

Two hot-path caches used `Dictionary` behind a `Lock`:

- `QueryCache` held an LRU of query results and incremented hit/miss
  counters inside the lock, coupling diagnostic counters to eviction.
- `TermDictionaryReader` held wildcard and fuzzy automaton caches with
  a lock-guarded read-through pattern. Automaton construction ran under
  the lock, serialising all concurrent fuzzy and wildcard queries.

## Decision

Replace `Dictionary`+`Lock` with `ConcurrentDictionary` and use
`Interlocked.CompareExchange` to swap in a fresh dictionary when the
soft entry cap is exceeded. For the wildcard automaton cache, wrap
values in `Lazy<T>` so only the constructing thread pays the build cost.

## Rationale

`ConcurrentDictionary.GetOrAdd` is lock-free for readers on the common
path. The generation-swap eviction strategy avoids `Clear()` calls
(which are unsafe under concurrent reads) and lets existing readers
finish with the old dictionary while new readers pick up the replacement.
The approach adds no allocations on the read path and removes two `Lock`
instances from the library.

## Consequences

- `QueryCache.TryGet` now increments hit/miss counters with
  `Interlocked.Increment` outside the LRU lock.
- `TermDictionaryReader` automaton caches use `ConcurrentDictionary`
  with `Lazy<WildcardAutomaton>` and generation-swap eviction.
- Any future read-heavy cache with a soft size cap should use this
  pattern rather than `Dictionary`+`Lock`+`Clear()`.
