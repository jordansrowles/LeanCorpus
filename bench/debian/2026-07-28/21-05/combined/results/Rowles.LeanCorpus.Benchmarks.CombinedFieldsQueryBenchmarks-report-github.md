```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                             | MinimumShouldMatch | DocumentCount | Mean       | Error    | StdDev   | Ratio | Gen0     | Gen1    | Allocated | Alloc Ratio |
|----------------------------------- |------------------- |-------------- |-----------:|---------:|---------:|------:|---------:|--------:|----------:|------------:|
| **LeanCorpus_CombinedFieldsQuery**     | **1**                  | **100000**        | **2,419.0 μs** | **16.37 μs** | **15.31 μs** |  **1.00** | **117.1875** | **11.7188** | **487.62 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 1                  | 100000        |   538.8 μs |  5.83 μs |  5.45 μs |  0.22 |   4.8828 |       - |  21.44 KB |        0.04 |
| LuceneNet_BooleanQuery_MultiField  | 1                  | 100000        |   686.9 μs |  5.22 μs |  4.63 μs |  0.28 | 186.5234 |  4.8828 | 771.69 KB |        1.58 |
|                                    |                    |               |            |          |          |       |          |         |           |             |
| **LeanCorpus_CombinedFieldsQuery**     | **2**                  | **100000**        | **2,433.7 μs** |  **7.25 μs** |  **6.05 μs** |  **1.00** | **117.1875** | **11.7188** | **487.62 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 2                  | 100000        |   535.1 μs |  5.98 μs |  5.30 μs |  0.22 |   4.8828 |       - |  21.45 KB |        0.04 |
| LuceneNet_BooleanQuery_MultiField  | 2                  | 100000        |   685.0 μs |  4.59 μs |  4.29 μs |  0.28 | 187.5000 |  3.9063 | 771.69 KB |        1.58 |
