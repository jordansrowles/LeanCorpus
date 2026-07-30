```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                                 | DocumentCount | Mean       | Error    | StdDev   | Gen0      | Gen1    | Allocated  |
|--------------------------------------- |-------------- |-----------:|---------:|---------:|----------:|--------:|-----------:|
| LeanCorpus_HybridHighlighter_NoOffsets | 100000        |   143.4 μs |  0.50 μs |  0.46 μs |   20.2637 |       - |   83.02 KB |
| LeanCorpus_Highlighter                 | 100000        |   101.7 μs |  0.45 μs |  0.42 μs |   15.2588 |       - |   62.66 KB |
| LuceneNet_Highlighter                  | 100000        |   137.1 μs |  0.78 μs |  0.73 μs |   56.3965 |       - |  230.47 KB |
| LeanCorpus_TermVectorHighlighter       | 100000        |   546.5 μs |  1.78 μs |  1.58 μs |   29.2969 |       - |  119.69 KB |
| LuceneNet_FastVectorHighlighter        | 100000        | 8,156.8 μs | 42.03 μs | 39.31 μs | 1101.5625 | 15.6250 | 4545.97 KB |
