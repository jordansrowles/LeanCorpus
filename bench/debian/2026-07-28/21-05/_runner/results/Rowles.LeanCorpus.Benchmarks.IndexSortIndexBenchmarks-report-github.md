```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  Job-AMZPBM : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2

IterationCount=5  WarmupCount=2  

```
| Method                    | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-------------- |---------:|---------:|---------:|------:|------------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_Index_Unsorted | 100000        |  8.367 s | 0.1619 s | 0.0251 s |  1.00 | 177000.0000 | 71000.0000 | 5000.0000 |   1.18 GB |        1.00 |
| LeanCorpus_Index_Sorted   | 100000        |  8.881 s | 0.0487 s | 0.0075 s |  1.06 | 181000.0000 | 71000.0000 | 5000.0000 |    1.2 GB |        1.02 |
| LuceneNet_Index_Unsorted  | 100000        | 10.172 s | 0.0970 s | 0.0252 s |  1.22 | 651000.0000 | 73000.0000 | 4000.0000 |   3.64 GB |        3.10 |
| LuceneNet_Index_Sorted    | 100000        |  9.568 s | 0.1538 s | 0.0399 s |  1.14 | 591000.0000 | 61000.0000 | 3000.0000 |   3.29 GB |        2.80 |
