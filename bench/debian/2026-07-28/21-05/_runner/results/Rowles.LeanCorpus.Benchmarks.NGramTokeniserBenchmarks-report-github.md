```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                                        | GramRange | DocumentCount | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0        | Allocated   | Alloc Ratio |
|---------------------------------------------- |---------- |-------------- |-----------:|---------:|---------:|------:|--------:|------------:|------------:|------------:|
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **2-3**       | **100000**        |   **284.1 ms** |  **1.51 ms** |  **1.41 ms** |  **1.00** |    **0.00** |           **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | 2-3       | 100000        |   310.4 ms |  3.57 ms |  3.34 ms |  1.09 |    0.01 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | 2-3       | 100000        |   442.2 ms |  2.00 ms |  1.87 ms |  1.56 |    0.01 |           - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | 2-3       | 100000        |   404.2 ms |  3.07 ms |  2.87 ms |  1.42 |    0.01 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | 2-3       | 100000        | 1,023.3 ms |  7.24 ms |  6.78 ms |  3.60 |    0.03 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | 2-3       | 100000        |   984.9 ms |  5.05 ms |  4.73 ms |  3.47 |    0.02 |           - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | 2-3       | 100000        |   919.8 ms |  7.59 ms |  7.10 ms |  3.24 |    0.03 | 211000.0000 | 885600000 B |          NA |
| LuceneNet_NGramTokenizer                      | 2-3       | 100000        | 5,738.6 ms | 14.34 ms | 13.41 ms | 20.20 |    0.11 | 211000.0000 | 885600000 B |          NA |
|                                               |           |               |            |          |          |       |         |             |             |             |
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **3-5**       | **100000**        |   **296.7 ms** |  **1.08 ms** |  **1.01 ms** |  **1.00** |    **0.00** |           **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | 3-5       | 100000        |   473.3 ms |  3.99 ms |  3.73 ms |  1.59 |    0.01 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | 3-5       | 100000        |   450.4 ms |  3.22 ms |  3.01 ms |  1.52 |    0.01 |           - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | 3-5       | 100000        |   419.7 ms |  2.55 ms |  2.39 ms |  1.41 |    0.01 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | 3-5       | 100000        | 1,564.2 ms |  5.58 ms |  5.22 ms |  5.27 |    0.02 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | 3-5       | 100000        | 1,003.3 ms |  2.89 ms |  2.71 ms |  3.38 |    0.01 |           - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | 3-5       | 100000        |   922.8 ms |  4.90 ms |  4.34 ms |  3.11 |    0.02 | 212000.0000 | 888000000 B |          NA |
| LuceneNet_NGramTokenizer                      | 3-5       | 100000        | 9,642.1 ms | 18.73 ms | 17.52 ms | 32.49 |    0.12 | 212000.0000 | 888000000 B |          NA |
