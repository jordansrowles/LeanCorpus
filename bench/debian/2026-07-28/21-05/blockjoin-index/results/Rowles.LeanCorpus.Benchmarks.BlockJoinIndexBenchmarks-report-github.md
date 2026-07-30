```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  Job-FEWCWF : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2

InvocationCount=1  IterationCount=3  UnrollFactor=1  
WarmupCount=1  

```
| Method                 | BlockCount | Mean    | Error   | StdDev  | Ratio | Gen0         | Gen1        | Gen2       | Allocated | Alloc Ratio |
|----------------------- |----------- |--------:|--------:|--------:|------:|-------------:|------------:|-----------:|----------:|------------:|
| LeanLucene_IndexBlocks | 100000     | 24.30 s | 2.914 s | 0.160 s |  1.00 |  492000.0000 | 197000.0000 | 16000.0000 |   3.18 GB |        1.00 |
| LuceneNet_IndexBlocks  | 100000     | 34.16 s | 1.054 s | 0.058 s |  1.41 | 1292000.0000 |  47000.0000 |  5000.0000 |   6.29 GB |        1.98 |
