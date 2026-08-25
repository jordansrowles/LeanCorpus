---
title: Benchmarks - Span queries
---

# Span queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 25 August 2026 10:21 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method               | SpanType | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------- |--------- |-------------- |-----------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_SpanQuery** | **Near**     | **100000**        |   **637.6 μs** | **1.92 μs** | **1.79 μs** |  **1.00** |  **9.7656** |      **-** |   **39.9 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Near     | 100000        |   496.7 μs | 0.70 μs | 0.66 μs |  0.78 | 44.9219 | 0.9766 |  188.4 KB |        4.72 |
|                      |          |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_SpanQuery** | **Not**      | **100000**        |   **692.9 μs** | **0.75 μs** | **0.71 μs** |  **1.00** |  **9.7656** |      **-** |  **40.84 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Not      | 100000        |   613.0 μs | 0.84 μs | 0.75 μs |  0.88 | 61.5234 | 1.9531 | 262.27 KB |        6.42 |
|                      |          |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_SpanQuery** | **Or**       | **100000**        |   **479.1 μs** | **1.26 μs** | **1.05 μs** |  **1.00** |  **0.9766** |      **-** |   **5.02 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Or       | 100000        | 1,939.8 μs | 2.67 μs | 2.49 μs |  4.05 | 41.0156 | 1.9531 | 171.76 KB |       34.19 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-span"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-span" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-span" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-span" style="max-width:960px"><canvas id="chart-bench-span" style="height:500px"></canvas></div>
<p><a href="span.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


