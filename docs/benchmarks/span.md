---
title: Benchmarks - Span queries
---

# Span queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method               | SpanType | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------- |--------- |-------------- |-----------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_SpanQuery** | **Near**     | **100000**        |   **742.1 μs** | **0.84 μs** | **0.78 μs** |  **1.00** |  **8.7891** |      **-** |  **38.17 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Near     | 100000        |   498.3 μs | 0.32 μs | 0.28 μs |  0.67 | 44.9219 | 0.9766 |  188.4 KB |        4.94 |
|                      |          |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_SpanQuery** | **Not**      | **100000**        |   **922.6 μs** | **0.78 μs** | **0.69 μs** |  **1.00** |  **8.7891** |      **-** |  **38.65 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Not      | 100000        |   613.6 μs | 1.16 μs | 0.97 μs |  0.67 | 61.5234 | 1.9531 | 262.27 KB |        6.79 |
|                      |          |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_SpanQuery** | **Or**       | **100000**        | **1,040.5 μs** | **1.50 μs** | **1.33 μs** |  **1.00** |       **-** |      **-** |   **2.83 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Or       | 100000        | 1,919.3 μs | 2.80 μs | 2.62 μs |  1.84 | 41.0156 | 1.9531 | 171.76 KB |       60.73 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-span"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-span" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-span" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-span" style="max-width:960px"><canvas id="chart-bench-span" style="height:500px"></canvas></div>
<p><a href="span.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


