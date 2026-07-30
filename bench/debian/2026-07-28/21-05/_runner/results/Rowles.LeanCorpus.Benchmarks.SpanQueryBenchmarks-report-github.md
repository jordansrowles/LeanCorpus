```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method               | SpanType | DocumentCount | Mean       | Error    | StdDev   | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------- |--------- |-------------- |-----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_SpanQuery** | **Near**     | **100000**        |   **745.2 μs** |  **3.70 μs** |  **3.46 μs** |  **1.00** |  **8.7891** |      **-** |  **38.18 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Near     | 100000        |   504.9 μs |  3.96 μs |  3.71 μs |  0.68 | 44.9219 | 0.9766 |  188.4 KB |        4.93 |
|                      |          |               |            |          |          |       |         |        |           |             |
| **LeanCorpus_SpanQuery** | **Not**      | **100000**        |   **943.1 μs** |  **1.83 μs** |  **1.62 μs** |  **1.00** |  **8.7891** |      **-** |  **38.66 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Not      | 100000        |   624.4 μs |  4.30 μs |  4.03 μs |  0.66 | 61.5234 | 1.9531 | 262.27 KB |        6.78 |
|                      |          |               |            |          |          |       |         |        |           |             |
| **LeanCorpus_SpanQuery** | **Or**       | **100000**        | **1,124.3 μs** |  **7.43 μs** |  **6.95 μs** |  **1.00** |       **-** |      **-** |   **2.84 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Or       | 100000        | 1,946.7 μs | 11.93 μs | 11.16 μs |  1.73 | 41.0156 | 1.9531 | 171.76 KB |       60.56 |
