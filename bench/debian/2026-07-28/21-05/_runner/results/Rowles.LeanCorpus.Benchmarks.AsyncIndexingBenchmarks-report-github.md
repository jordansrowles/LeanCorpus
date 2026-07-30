```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  Job-AMZPBM : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2

IterationCount=5  WarmupCount=2  

```
| Method                                 | DocumentCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |---------:|---------:|---------:|------:|--------:|------------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_AddDocument_Sync            | 100000        |  7.914 s | 0.2803 s | 0.0728 s |  1.00 |    0.00 | 165000.0000 | 69000.0000 | 5000.0000 |   1.07 GB |        1.00 |
| LeanCorpus_AddDocumentAsync_Sequential | 100000        | 11.608 s | 0.6014 s | 0.1562 s |  1.47 |    0.02 | 197000.0000 | 89000.0000 | 6000.0000 |   1.31 GB |        1.23 |
| LeanCorpus_AddDocumentsAsync_Batch     | 100000        | 12.004 s | 1.0192 s | 0.1577 s |  1.52 |    0.02 | 196000.0000 | 90000.0000 | 6000.0000 |    1.3 GB |        1.22 |
