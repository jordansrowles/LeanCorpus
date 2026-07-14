# ADR002: Single auto-vectorised SIMD path

- **Date:** 2026-06-16
- **Status:** Accepted

## Context

LeanCorpus maintained two SIMD implementations for vector arithmetic
operations (cosine similarity, dot product, L2 distance). `SimdVectorOps`
used `System.Numerics.Vector<T>` and relied on the JIT to auto-vectorise
to the host's native register width. `SimdIntrinsicsVectorOps` used
explicit `Avx512F`, `Avx2`/`Fma`, and `Sse` intrinsics with a runtime
dispatch cascade. The XML documentation on `SimdIntrinsicsVectorOps`
stated that the slower of the two would be removed once measurements
were concluded.

## Decision

Remove the explicit intrinsics path and retain only `SimdVectorOps`.

## Rationale

Benchmarked on Debian 13, Xeon E3-1220 V2 (Ivy Bridge; AVX only;
`VectorSize=128`), .NET 10.0.3. Cosine similarity across five
dimensions:

| Dimension | `Vector<T>` | Intrinsics | Ratio |
|-----------|------------|------------|-------|
| 64 | 25.7 us | 35.8 us | 1.39x |
| 128 | 41.1 us | 50.7 us | 1.23x |
| 256 | 70.4 us | 81.3 us | 1.15x |
| 512 | 125.0 us | 177.3 us | 1.42x |
| 1024 | 255.3 us | 247.8 us | tie |

`Vector<T>` was faster or tied at every dimension. No production code
called `SimdIntrinsicsVectorOps`. The auto-vectorised path is shorter,
more maintainable, and benefits from JIT improvements with each .NET
release without source changes.

## Consequences

- `SimdIntrinsicsVectorOps` deleted along with its integration tests and
  the comparison benchmark.
- All vector arithmetic in the library uses `SimdVectorOps`. Future SIMD
  work defaults to `Vector<T>`. Explicit intrinsics remain an option only
  if profiling on a specific platform shows `Vector<T>` underperforming
  and the gap is material to end-to-end query latency.
