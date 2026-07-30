```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                | Scenario            | DocumentCount | Mean          | Error      | StdDev     | Ratio    | RatioSD | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|---------------------- |-------------------- |-------------- |--------------:|-----------:|-----------:|---------:|--------:|---------:|---------:|-----------:|------------:|
| **LeanCorpus_FuzzyQuery** | **long-edit1-common**   | **100000**        |     **60.918 μs** |  **0.5212 μs** |  **0.4875 μs** |     **1.00** |    **0.00** |   **0.3662** |        **-** |    **1.61 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | long-edit1-common   | 100000        |  1,012.451 μs |  8.8201 μs |  8.2503 μs |    16.62 |    0.18 |  78.1250 |   1.9531 |  326.49 KB |      202.87 |
|                       |                     |               |               |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **medium-edit1-common** | **100000**        |    **148.372 μs** |  **0.8762 μs** |  **0.8196 μs** |     **1.00** |    **0.00** |   **0.4883** |        **-** |    **2.39 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | medium-edit1-common | 100000        |  1,395.695 μs |  9.7289 μs |  9.1004 μs |     9.41 |    0.08 | 242.1875 |   5.8594 |  991.89 KB |      414.91 |
|                       |                     |               |               |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **medium-edit2-common** | **100000**        |    **216.795 μs** |  **1.1462 μs** |  **1.0721 μs** |     **1.00** |    **0.00** |   **0.7324** |        **-** |    **3.76 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | medium-edit2-common | 100000        | 10,605.651 μs | 42.6616 μs | 39.9056 μs |    48.92 |    0.30 | 500.0000 | 156.2500 | 2381.52 KB |      633.75 |
|                       |                     |               |               |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **nohit-edit2**         | **100000**        |      **1.570 μs** |  **0.0089 μs** |  **0.0083 μs** |     **1.00** |    **0.00** |   **0.2823** |        **-** |    **1.16 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | nohit-edit2         | 100000        |  6,600.960 μs | 33.1534 μs | 31.0117 μs | 4,203.63 |   28.88 | 523.4375 | 226.5625 | 2511.04 KB |    2,171.71 |
|                       |                     |               |               |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **short-edit1-common**  | **100000**        |    **519.933 μs** |  **1.7540 μs** |  **1.6407 μs** |     **1.00** |    **0.00** |   **0.9766** |        **-** |    **6.73 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | short-edit1-common  | 100000        |  2,293.314 μs | 17.6525 μs | 16.5121 μs |     4.41 |    0.03 | 296.8750 |  15.6250 | 1247.33 KB |      185.43 |
