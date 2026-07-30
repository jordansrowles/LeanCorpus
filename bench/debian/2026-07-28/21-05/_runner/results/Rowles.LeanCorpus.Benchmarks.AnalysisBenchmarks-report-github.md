```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method             | DocumentCount | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0        | Allocated   | Alloc Ratio |
|------------------- |-------------- |-----------:|---------:|---------:|------:|--------:|------------:|------------:|------------:|
| LeanCorpus_Analyse | 100000        |   896.4 ms |  6.56 ms |  6.13 ms |  1.00 |    0.00 |           - |           - |          NA |
| LuceneNet_Analyse  | 100000        | 2,237.0 ms | 12.66 ms | 11.84 ms |  2.50 |    0.02 | 144000.0000 | 605284312 B |          NA |
