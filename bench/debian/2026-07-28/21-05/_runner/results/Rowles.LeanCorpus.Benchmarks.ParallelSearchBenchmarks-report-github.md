```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                         | SegmentCount | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0     | Gen1    | Allocated | Alloc Ratio |
|------------------------------- |------------- |-------------- |---------:|--------:|--------:|------:|---------:|--------:|----------:|------------:|
| **&#39;LeanCorpus phrase sequential&#39;** | **4**            | **100000**        | **785.2 μs** | **4.43 μs** | **4.14 μs** |  **1.00** |   **4.8828** |       **-** |  **20.14 KB** |        **1.00** |
| &#39;LeanCorpus phrase parallel&#39;   | 4            | 100000        | 531.7 μs | 5.59 μs | 5.23 μs |  0.68 |   5.8594 |       - |  23.64 KB |        1.17 |
| &#39;Lucene.NET phrase sequential&#39; | 4            | 100000        | 343.4 μs | 1.12 μs | 1.05 μs |  0.44 |  75.6836 | 13.6719 | 310.49 KB |       15.42 |
|                                |              |               |          |         |         |       |          |         |           |             |
| **&#39;LeanCorpus phrase sequential&#39;** | **8**            | **100000**        | **776.9 μs** | **3.07 μs** | **2.88 μs** |  **1.00** |   **6.8359** |       **-** |   **30.8 KB** |        **1.00** |
| &#39;LeanCorpus phrase parallel&#39;   | 8            | 100000        | 500.4 μs | 4.70 μs | 4.40 μs |  0.64 |   7.8125 |       - |  34.86 KB |        1.13 |
| &#39;Lucene.NET phrase sequential&#39; | 8            | 100000        | 356.1 μs | 1.22 μs | 1.14 μs |  0.46 | 117.1875 | 19.5313 | 480.88 KB |       15.61 |
