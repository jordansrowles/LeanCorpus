---
title: Benchmarks - Span queries
---

# Span queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method               | SpanType | DocumentCount | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------- |--------- |-------------- |----------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_SpanQuery** | **Near**     | **100000**        |  **72.42 μs** | **1.365 μs** | **2.354 μs** |  **1.00** |    **0.00** |  **8.3008** |      **-** |  **33.08 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Near     | 100000        | 150.50 μs | 0.174 μs | 0.163 μs |  2.08 |    0.07 | 15.1367 | 0.4883 |  62.93 KB |        1.90 |
|                      |          |               |           |          |          |       |         |         |        |           |             |
| **LeanCorpus_SpanQuery** | **Not**      | **100000**        |  **98.93 μs** | **1.975 μs** | **5.406 μs** |  **1.00** |    **0.00** |  **8.4229** |      **-** |  **33.63 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Not      | 100000        | 205.14 μs | 0.526 μs | 0.492 μs |  2.08 |    0.11 | 21.9727 | 0.4883 |   93.7 KB |        2.79 |
|                      |          |               |           |          |          |       |         |         |        |           |             |
| **LeanCorpus_SpanQuery** | **Or**       | **100000**        | **107.45 μs** | **2.396 μs** | **6.988 μs** |  **1.00** |    **0.00** |  **1.9531** |      **-** |   **8.35 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Or       | 100000        | 987.01 μs | 1.393 μs | 1.088 μs |  9.22 |    0.59 | 17.5781 | 1.9531 |  72.24 KB |        8.66 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-span"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-span" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-span" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-span" style="max-width:960px"><canvas id="chart-bench-span" style="height:500px"></canvas></div>
<p><a href="debian-span.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


