---
title: Benchmarks - Analysis filters
---

# Analysis filters

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method           | Scenario             | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------- |--------------------- |------------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Apply** | **caching**              |   **705.08 ns** |  **9.857 ns** |  **8.231 ns** |  **1.00** |    **0.00** | **0.0238** | **0.0114** |     **152 B** |        **1.00** |
| LuceneNet_Apply  | caching              | 1,990.44 ns |  3.936 ns |  3.489 ns |  2.82 |    0.03 | 2.3689 |      - |    9912 B |       65.21 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **classic-mutating**     |   **144.88 ns** |  **0.283 ns** |  **0.264 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | classic-mutating     | 2,593.02 ns |  3.298 ns |  2.754 ns | 17.90 |    0.04 | 2.4910 | 0.0038 |   10424 B |      434.33 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **classic-noop**         |    **57.89 ns** |  **0.046 ns** |  **0.043 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | classic-noop         | 2,384.56 ns |  5.736 ns |  5.365 ns | 41.19 |    0.09 | 2.4910 | 0.0038 |   10424 B |      434.33 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **common-grams**         |   **322.84 ns** |  **0.487 ns** |  **0.432 ns** |  **1.00** |    **0.00** | **0.0591** |      **-** |     **248 B** |        **1.00** |
| LuceneNet_Apply  | common-grams         | 8,911.10 ns | 13.186 ns | 11.689 ns | 27.60 |    0.05 | 3.2501 |      - |   13648 B |       55.03 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **hyphenated-words**     |    **47.26 ns** |  **0.047 ns** |  **0.040 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | hyphenated-words     | 1,977.46 ns |  9.123 ns |  8.534 ns | 41.85 |    0.18 | 2.4300 |      - |   10176 B |      424.00 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **patte(...)ating [24]** |   **442.95 ns** |  **0.420 ns** |  **0.373 ns** |  **1.00** |    **0.00** | **0.0191** |      **-** |      **80 B** |        **1.00** |
| LuceneNet_Apply  | patte(...)ating [24] | 5,168.45 ns | 13.535 ns | 11.998 ns | 11.67 |    0.03 | 3.0518 |      - |   12793 B |      159.91 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **pattern-replace-noop** |   **100.75 ns** |  **0.081 ns** |  **0.067 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | pattern-replace-noop | 4,329.02 ns |  8.246 ns |  7.310 ns | 42.97 |    0.08 | 3.0289 |      - |   12681 B |      528.38 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-analysis-filters"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-analysis-filters" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-analysis-filters" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-analysis-filters" style="max-width:960px"><canvas id="chart-bench-analysis-filters" style="height:500px"></canvas></div>
<p><a href="debian-analysis-filters.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


