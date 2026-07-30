```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                                 | DocumentCount | Mean          | Error      | StdDev     | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |--------------:|-----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| &#39;LeanCorpus acquire, search, release&#39;  | 100000        | 109,950.22 ns | 574.482 ns | 537.370 ns | 1.000 |  0.1221 |      - |     939 B |        1.00 |
| &#39;LeanCorpus lease, search, release&#39;    | 100000        | 110,132.17 ns | 649.521 ns | 607.562 ns | 1.002 |  0.1221 |      - |     952 B |        1.01 |
| &#39;Lucene.NET acquire, search, release&#39;  | 100000        | 150,781.48 ns | 739.547 ns | 691.773 ns | 1.371 | 11.9629 | 0.2441 |   51220 B |       54.55 |
| &#39;LeanCorpus acquire and release&#39;       | 100000        |      24.74 ns |   0.162 ns |   0.152 ns | 0.000 |       - |      - |         - |        0.00 |
| &#39;LeanCorpus lease acquire and release&#39; | 100000        |      30.25 ns |   0.077 ns |   0.072 ns | 0.000 |  0.0153 |      - |      64 B |        0.07 |
| &#39;Lucene.NET acquire and release&#39;       | 100000        |      30.79 ns |   0.123 ns |   0.103 ns | 0.000 |       - |      - |         - |        0.00 |
