```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method              | Scenario         | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1   | Allocated | Alloc Ratio |
|-------------------- |----------------- |-----------:|----------:|----------:|------:|--------:|----------:|-------:|----------:|------------:|
| **LeanCorpus_Tokenise** | **comma-long**       |  **63.723 μs** | **0.4094 μs** | **0.3830 μs** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | comma-long       | 542.808 μs | 2.7108 μs | 2.5357 μs |  8.52 |    0.06 | 1091.7969 | 0.9766 | 4559840 B |          NA |
|                     |                  |            |           |           |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **comma-short**      |   **1.121 μs** | **0.0049 μs** | **0.0046 μs** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | comma-short      |   4.150 μs | 0.0242 μs | 0.0227 μs |  3.70 |    0.02 |    5.0964 |      - |   21344 B |          NA |
|                     |                  |            |           |           |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **whitespace-long**  |  **26.307 μs** | **0.1044 μs** | **0.0976 μs** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | whitespace-long  | 102.808 μs | 0.8991 μs | 0.8410 μs |  3.91 |    0.03 |  144.7754 | 0.1221 |  605960 B |          NA |
|                     |                  |            |           |           |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **whitespace-short** |   **1.139 μs** | **0.0052 μs** | **0.0049 μs** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | whitespace-short |   4.539 μs | 0.0307 μs | 0.0287 μs |  3.98 |    0.03 |    5.1804 |      - |   21696 B |          NA |
