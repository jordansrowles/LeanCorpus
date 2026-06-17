---
title: Benchmarks - Term queries
---

# Term queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method               | QueryTerm  | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------- |----------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_TermQuery** | **government** | **100000**        | **127.9 μs** | **0.84 μs** | **0.78 μs** |  **1.00** |       **-** |      **-** |     **720 B** |        **1.00** |
| LuceneNet_TermQuery  | government | 100000        | 163.7 μs | 1.20 μs | 1.06 μs |  1.28 | 11.9629 | 0.2441 |   51215 B |       71.13 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_TermQuery** | **people**     | **100000**        | **180.5 μs** | **0.81 μs** | **0.72 μs** |  **1.00** |       **-** |      **-** |     **712 B** |        **1.00** |
| LuceneNet_TermQuery  | people     | 100000        | 201.1 μs | 1.08 μs | 1.01 μs |  1.11 | 11.4746 | 0.2441 |   48994 B |       68.81 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_TermQuery** | **said**       | **100000**        | **857.5 μs** | **5.55 μs** | **5.19 μs** |  **1.00** |       **-** |      **-** |     **704 B** |        **1.00** |
| LuceneNet_TermQuery  | said       | 100000        | 793.9 μs | 5.31 μs | 4.96 μs |  0.93 | 11.7188 |      - |   49026 B |       69.64 |

