---
title: Benchmarks - Block-Join
---

# Block-Join

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c89728e` &nbsp;&middot;&nbsp; 15 May 2026 21:48 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                           | BlockCount | Mean          | Error       | StdDev      | Ratio | Gen0      | Gen1     | Allocated  | Alloc Ratio |
|--------------------------------- |----------- |--------------:|------------:|------------:|------:|----------:|---------:|-----------:|------------:|
| LeanCorpus_IndexBlocks           | 500        | 77,636.639 μs | 241.4796 μs | 225.8801 μs | 1.000 | 1714.2857 | 857.1429 | 13113088 B |       1.000 |
| LeanCorpus_BlockJoinQuery        | 500        |      6.914 μs |   0.0084 μs |   0.0079 μs | 0.000 |    0.1678 |        - |      720 B |       0.000 |
| LuceneNet_IndexBlocks            | 500        | 54,143.770 μs | 299.7626 μs | 280.3981 μs | 0.697 | 5000.0000 | 500.0000 | 28715742 B |       2.190 |
| LuceneNet_ToParentBlockJoinQuery | 500        |     20.816 μs |   0.0583 μs |   0.0545 μs | 0.000 |    3.0518 |        - |    12888 B |       0.001 |

