---
title: Benchmarks - Boolean queries
---

# Boolean queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | BooleanShape  | DocumentCount | Mean      | Error    | StdDev    | Median    | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------ |-------------- |-------------- |----------:|---------:|----------:|----------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_BooleanQuery** | **Must2Common**   | **100000**        | **234.85 μs** | **1.134 μs** |  **1.006 μs** | **235.07 μs** |  **1.00** |    **0.00** |  **3.1738** |      **-** |   **13.5 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must2Common   | 100000        | 467.88 μs | 0.673 μs |  0.597 μs | 467.96 μs |  1.99 |    0.01 | 12.2070 | 0.4883 |  51.32 KB |        3.80 |
|                         |               |               |           |          |           |           |       |         |         |        |           |             |
| **LeanCorpus_BooleanQuery** | **Must3Mixed**    | **100000**        |  **33.50 μs** | **0.815 μs** |  **2.391 μs** |  **32.82 μs** |  **1.00** |    **0.00** |  **3.8452** |      **-** |  **15.45 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must3Mixed    | 100000        | 123.72 μs | 0.355 μs |  0.332 μs | 123.84 μs |  3.71 |    0.25 | 17.3340 | 0.2441 |  73.16 KB |        4.74 |
|                         |               |               |           |          |           |           |       |         |         |        |           |             |
| **LeanCorpus_BooleanQuery** | **MustNotCommon** | **100000**        |  **82.81 μs** | **1.641 μs** |  **4.235 μs** |  **83.16 μs** |  **1.00** |    **0.00** |  **3.2959** |      **-** |  **13.48 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | MustNotCommon | 100000        | 276.84 μs | 1.771 μs |  1.657 μs | 275.89 μs |  3.35 |    0.17 | 13.1836 | 0.4883 |  54.31 KB |        4.03 |
|                         |               |               |           |          |           |           |       |         |         |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should2Common** | **100000**        | **114.84 μs** | **6.070 μs** | **17.898 μs** | **123.17 μs** |  **1.00** |    **0.00** |  **3.2959** |      **-** |  **13.64 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should2Common | 100000        |        NA |       NA |        NA |        NA |     ? |       ? |      NA |     NA |        NA |           ? |
|                         |               |               |           |          |           |           |       |         |         |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should4Mixed**  | **100000**        |        **NA** |       **NA** |        **NA** |        **NA** |     **?** |       **?** |      **NA** |     **NA** |        **NA** |           **?** |
| LuceneNet_BooleanQuery  | Should4Mixed  | 100000        |        NA |       NA |        NA |        NA |     ? |       ? |      NA |     NA |        NA |           ? |

Benchmarks with issues:
  BooleanQueryBenchmarks.LuceneNet_BooleanQuery: DefaultJob [BooleanShape=Should2Common, DocumentCount=100000]
  BooleanQueryBenchmarks.LeanCorpus_BooleanQuery: DefaultJob [BooleanShape=Should4Mixed, DocumentCount=100000]
  BooleanQueryBenchmarks.LuceneNet_BooleanQuery: DefaultJob [BooleanShape=Should4Mixed, DocumentCount=100000]

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-boolean"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-boolean" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-boolean" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-boolean" style="max-width:960px"><canvas id="chart-bench-boolean" style="height:500px"></canvas></div>
<p><a href="debian-boolean.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


