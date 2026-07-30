```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                         | MaxSnippetLength | DocumentCount | Mean        | Error      | StdDev       | Ratio | RatioSD | Gen0      | Gen1   | Allocated  | Alloc Ratio |
|------------------------------- |----------------- |-------------- |------------:|-----------:|-------------:|------:|--------:|----------:|-------:|-----------:|------------:|
| **LeanCorpus_Highlight_TwoTerms**  | **100**              | **100000**        |    **66.88 μs** |   **0.677 μs** |     **0.633 μs** |  **1.00** |    **0.00** |   **11.2305** |      **-** |   **46.06 KB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 100              | 100000        |   167.04 μs |   1.797 μs |     1.681 μs |  2.50 |    0.03 |   10.4980 |      - |   42.97 KB |        0.93 |
| LuceneNet_Highlight_TwoTerms   | 100              | 100000        | 4,193.29 μs | 612.204 μs | 1,805.098 μs | 62.70 |   26.87 | 1257.8125 | 7.8125 | 5144.61 KB |      111.70 |
| LuceneNet_Highlight_FiveTerms  | 100              | 100000        | 4,348.34 μs | 618.365 μs | 1,823.263 μs | 65.02 |   27.14 | 1312.5000 | 7.8125 | 5389.79 KB |      117.02 |
|                                |                  |               |             |            |              |       |         |           |        |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **200**              | **100000**        |    **69.42 μs** |   **0.759 μs** |     **0.710 μs** |  **1.00** |    **0.00** |   **16.7236** |      **-** |   **68.63 KB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 200              | 100000        |   172.32 μs |   2.040 μs |     1.908 μs |  2.48 |    0.04 |   15.1367 |      - |   61.92 KB |        0.90 |
| LuceneNet_Highlight_TwoTerms   | 200              | 100000        | 4,189.39 μs | 609.834 μs | 1,798.109 μs | 60.36 |   25.79 | 1257.8125 | 7.8125 | 5144.61 KB |       74.96 |
| LuceneNet_Highlight_FiveTerms  | 200              | 100000        | 4,397.65 μs | 624.479 μs | 1,841.291 μs | 63.36 |   26.41 | 1312.5000 | 7.8125 | 5389.79 KB |       78.53 |
|                                |                  |               |             |            |              |       |         |           |        |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **500**              | **100000**        |    **73.00 μs** |   **0.734 μs** |     **0.687 μs** |  **1.00** |    **0.00** |   **24.1699** |      **-** |   **99.19 KB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 500              | 100000        |   177.98 μs |   2.256 μs |     2.111 μs |  2.44 |    0.04 |   22.7051 |      - |   92.77 KB |        0.94 |
| LuceneNet_Highlight_TwoTerms   | 500              | 100000        | 4,167.12 μs | 607.255 μs | 1,790.506 μs | 57.09 |   24.42 | 1257.8125 | 7.8125 | 5144.61 KB |       51.87 |
| LuceneNet_Highlight_FiveTerms  | 500              | 100000        | 4,334.96 μs | 614.053 μs | 1,810.549 μs | 59.38 |   24.69 | 1312.5000 | 7.8125 | 5389.79 KB |       54.34 |
