---
title: Benchmarks - Span queries
---

# Span queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method               | SpanType | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------- |--------- |-------------- |-----------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_SpanQuery** | **Near**     | **100000**        |   **746.5 μs** | **0.43 μs** | **0.36 μs** |  **1.00** |  **8.7891** |      **-** |  **38.02 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Near     | 100000        |   495.7 μs | 0.69 μs | 0.58 μs |  0.66 | 44.9219 | 0.9766 |  188.4 KB |        4.95 |
|                      |          |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_SpanQuery** | **Not**      | **100000**        |   **938.4 μs** | **1.63 μs** | **1.53 μs** |  **1.00** |  **8.7891** |      **-** |  **38.42 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Not      | 100000        |   607.8 μs | 0.41 μs | 0.35 μs |  0.65 | 61.5234 | 1.9531 | 262.27 KB |        6.83 |
|                      |          |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_SpanQuery** | **Or**       | **100000**        | **1,109.2 μs** | **1.30 μs** | **1.21 μs** |  **1.00** |       **-** |      **-** |    **2.6 KB** |        **1.00** |
| LuceneNet_SpanQuery  | Or       | 100000        | 1,914.1 μs | 4.62 μs | 4.32 μs |  1.73 | 41.0156 | 1.9531 | 171.72 KB |       66.01 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-span"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-span" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-span" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-span" style="max-width:960px"><canvas id="chart-bench-span" style="height:500px"></canvas></div>
<p><a href="span.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


