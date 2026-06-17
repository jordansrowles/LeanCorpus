---
title: Benchmarks - Hunspell
---

# Hunspell

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method           | Mean     | Error   | StdDev  | Gen0   | Allocated |
|----------------- |---------:|--------:|--------:|-------:|----------:|
| Parse_Dictionary | 306.4 ns | 0.68 ns | 0.53 ns | 0.0420 |     176 B |
| Stem_Words       | 103.9 ns | 0.55 ns | 0.52 ns |      - |         - |

