```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  Job-AMZPBM : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2

IterationCount=5  WarmupCount=2  

```
| Method                    | Profile         | DocumentCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0        | Gen1       | Gen2      | Allocated  | Alloc Ratio |
|-------------------------- |---------------- |-------------- |---------:|---------:|---------:|------:|--------:|------------:|-----------:|----------:|-----------:|------------:|
| **LeanCorpus_IndexDocuments** | **PostingsOnly**    | **100000**        |  **6.791 s** | **0.0819 s** | **0.0127 s** |  **1.00** |    **0.00** | **117000.0000** | **63000.0000** | **7000.0000** |  **796.14 MB** |        **1.00** |
| LuceneNet_IndexDocuments  | PostingsOnly    | 100000        | 11.078 s | 0.5858 s | 0.0906 s |  1.63 |    0.01 | 368000.0000 | 17000.0000 | 2000.0000 | 1864.34 MB |        2.34 |
|                           |                 |               |          |          |          |       |         |             |            |           |            |             |
| **LeanCorpus_IndexDocuments** | **StoredFields**    | **100000**        |  **7.583 s** | **0.2873 s** | **0.0746 s** |  **1.00** |    **0.00** | **155000.0000** | **67000.0000** | **6000.0000** |  **998.95 MB** |        **1.00** |
| LuceneNet_IndexDocuments  | StoredFields    | 100000        | 13.291 s | 0.4817 s | 0.0745 s |  1.75 |    0.02 | 444000.0000 | 17000.0000 | 2000.0000 | 2222.89 MB |        2.23 |
|                           |                 |               |          |          |          |       |         |             |            |           |            |             |
| **LeanCorpus_IndexDocuments** | **SortedDocValues** | **100000**        |  **6.883 s** | **0.1072 s** | **0.0278 s** |  **1.00** |    **0.00** | **117000.0000** | **61000.0000** | **6000.0000** |  **814.03 MB** |        **1.00** |
| LuceneNet_IndexDocuments  | SortedDocValues | 100000        | 11.742 s | 0.4942 s | 0.1283 s |  1.71 |    0.02 | 368000.0000 | 17000.0000 | 2000.0000 | 1894.23 MB |        2.33 |
