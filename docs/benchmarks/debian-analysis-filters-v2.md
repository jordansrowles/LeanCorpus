---
title: Benchmarks - Analysis filters v2
---

# Analysis filters v2

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method           | Scenario             | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------- |--------------------- |------------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Apply** | **caching**              |   **704.69 ns** | **11.447 ns** |  **9.559 ns** |  **1.00** |    **0.00** | **0.0238** | **0.0114** |     **152 B** |        **1.00** |
| LuceneNet_Apply  | caching              | 1,890.11 ns |  4.564 ns |  3.563 ns |  2.68 |    0.04 | 2.3689 |      - |    9912 B |       65.21 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **classic-mutating**     |   **141.91 ns** |  **0.059 ns** |  **0.049 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | classic-mutating     | 2,556.40 ns |  2.828 ns |  2.645 ns | 18.01 |    0.02 | 2.4910 | 0.0038 |   10424 B |      434.33 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **classic-noop**         |    **57.41 ns** |  **0.053 ns** |  **0.050 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | classic-noop         | 2,336.22 ns |  6.167 ns |  5.467 ns | 40.70 |    0.10 | 2.4910 | 0.0038 |   10424 B |      434.33 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **common-grams**         |   **324.26 ns** |  **0.529 ns** |  **0.494 ns** |  **1.00** |    **0.00** | **0.0591** |      **-** |     **248 B** |        **1.00** |
| LuceneNet_Apply  | common-grams         | 8,830.79 ns | 10.516 ns |  8.781 ns | 27.23 |    0.05 | 3.2501 |      - |   13648 B |       55.03 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **hyphenated-words**     |    **47.08 ns** |  **0.062 ns** |  **0.052 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | hyphenated-words     | 1,995.21 ns |  4.672 ns |  4.370 ns | 42.38 |    0.10 | 2.4300 |      - |   10176 B |      424.00 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **patte(...)ating [24]** |   **447.24 ns** |  **0.614 ns** |  **0.513 ns** |  **1.00** |    **0.00** | **0.0191** |      **-** |      **80 B** |        **1.00** |
| LuceneNet_Apply  | patte(...)ating [24] | 5,178.00 ns | 14.351 ns | 13.424 ns | 11.58 |    0.03 | 3.0518 |      - |   12793 B |      159.91 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **pattern-replace-noop** |   **102.79 ns** |  **0.072 ns** |  **0.056 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | pattern-replace-noop | 4,423.75 ns | 11.125 ns | 10.406 ns | 43.04 |    0.10 | 3.0289 |      - |   12681 B |      528.38 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-analysis-filters-v2"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-analysis-filters-v2" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-analysis-filters-v2" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-analysis-filters-v2" style="max-width:960px"><canvas id="chart-bench-analysis-filters-v2" style="height:500px"></canvas></div>
<p><a href="debian-analysis-filters-v2.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


