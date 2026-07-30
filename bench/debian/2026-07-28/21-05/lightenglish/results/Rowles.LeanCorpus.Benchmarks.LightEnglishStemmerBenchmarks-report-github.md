```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                        | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0          | Gen1      | Allocated      | Alloc Ratio |
|------------------------------ |-------------- |------------:|---------:|---------:|------:|--------:|--------------:|----------:|---------------:|------------:|
| LightEnglish_Stem             | 100000        |    907.0 ms | 17.05 ms | 15.11 ms |  1.00 |    0.00 |             - |         - |              - |          NA |
| Porter_Stem                   | 100000        |  1,071.4 ms |  4.81 ms |  4.50 ms |  1.18 |    0.02 |             - |         - |              - |          NA |
| &#39;Lucene.NET PorterStemFilter&#39; | 100000        | 32,023.9 ms | 79.97 ms | 70.89 ms | 35.32 |    0.56 | 34076000.0000 | 3000.0000 | 142556149064 B |          NA |
