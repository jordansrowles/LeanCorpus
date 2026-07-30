```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                                   | DocumentCount | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1    | Allocated  | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|----------:|----------:|------:|--------:|---------:|--------:|-----------:|------------:|
| &#39;LeanCorpus MLT Scalar (DefaultParams)&#39;  | 100000        | 2.525 ms | 0.0465 ms | 0.0435 ms |  1.00 |    0.00 |  27.3438 |  7.8125 |  115.81 KB |        1.00 |
| &#39;LeanCorpus MLT Scalar (HighMinDocFreq)&#39; | 100000        | 2.505 ms | 0.0488 ms | 0.0801 ms |  0.99 |    0.04 |   7.8125 |       - |   37.56 KB |        0.32 |
| &#39;LeanCorpus MLT Scalar (NoBoost)&#39;        | 100000        | 2.504 ms | 0.0485 ms | 0.0498 ms |  0.99 |    0.03 |  27.3438 |  3.9063 |  114.62 KB |        0.99 |
| &#39;LeanCorpus MLT WAND (DefaultParams)&#39;    | 100000        | 2.775 ms | 0.0316 ms | 0.0280 ms |  1.10 |    0.02 |  46.8750 | 11.7188 |  198.11 KB |        1.71 |
| LuceneNet_MoreLikeThis_DefaultParams     | 100000        | 4.394 ms | 0.0626 ms | 0.0586 ms |  1.74 |    0.04 | 851.5625 | 23.4375 | 3569.54 KB |       30.82 |
| LuceneNet_MoreLikeThis_HighMinDocFreq    | 100000        | 3.384 ms | 0.0124 ms | 0.0116 ms |  1.34 |    0.02 | 281.2500 | 11.7188 |  1183.6 KB |       10.22 |
| LuceneNet_MoreLikeThis_NoBoost           | 100000        | 4.256 ms | 0.0625 ms | 0.0584 ms |  1.69 |    0.04 | 835.9375 | 23.4375 |  3569.3 KB |       30.82 |
