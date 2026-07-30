```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                          | Mean         | Error       | StdDev      | Gen0    | Gen1    | Allocated |
|-------------------------------- |-------------:|------------:|------------:|--------:|--------:|----------:|
| Parse_Dictionary                |     303.1 ns |     1.56 ns |     1.45 ns |  0.0420 |       - |     176 B |
| Stem_Words                      |     102.9 ns |     0.30 ns |     0.28 ns |       - |       - |         - |
| &#39;Lucene.NET HunspellStemFilter&#39; | 583,990.0 ns | 2,340.91 ns | 2,189.69 ns | 69.3359 | 13.6719 |  291232 B |
