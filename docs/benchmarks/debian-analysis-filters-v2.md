---
title: Benchmarks - Analysis filters v2
---

# Analysis filters v2

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method           | Scenario             | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------- |--------------------- |------------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Apply** | **caching**              |   **698.53 ns** |  **9.735 ns** |  **8.129 ns** |  **1.00** |    **0.00** | **0.0238** | **0.0114** |     **152 B** |        **1.00** |
| LuceneNet_Apply  | caching              | 1,902.50 ns |  2.901 ns |  2.423 ns |  2.72 |    0.03 | 2.3708 |      - |    9912 B |       65.21 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **classic-mutating**     |   **141.82 ns** |  **0.180 ns** |  **0.159 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | classic-mutating     | 2,522.17 ns |  7.473 ns |  6.990 ns | 17.78 |    0.05 | 2.4910 | 0.0038 |   10424 B |      434.33 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **classic-noop**         |    **57.53 ns** |  **0.059 ns** |  **0.049 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | classic-noop         | 2,379.36 ns |  3.891 ns |  3.249 ns | 41.36 |    0.06 | 2.4910 | 0.0038 |   10424 B |      434.33 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **common-grams**         |   **318.63 ns** |  **0.376 ns** |  **0.352 ns** |  **1.00** |    **0.00** | **0.0591** |      **-** |     **248 B** |        **1.00** |
| LuceneNet_Apply  | common-grams         | 8,752.66 ns | 14.786 ns | 13.831 ns | 27.47 |    0.05 | 3.2501 |      - |   13648 B |       55.03 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **hyphenated-words**     |    **48.56 ns** |  **0.793 ns** |  **0.703 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | hyphenated-words     | 1,986.89 ns |  4.171 ns |  3.902 ns | 40.93 |    0.59 | 2.4300 |      - |   10176 B |      424.00 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **patte(...)ating [24]** |   **443.38 ns** |  **0.879 ns** |  **0.822 ns** |  **1.00** |    **0.00** | **0.0191** |      **-** |      **80 B** |        **1.00** |
| LuceneNet_Apply  | patte(...)ating [24] | 5,055.06 ns | 12.161 ns | 11.375 ns | 11.40 |    0.03 | 3.0518 |      - |   12793 B |      159.91 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **pattern-replace-noop** |   **100.76 ns** |  **0.107 ns** |  **0.090 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | pattern-replace-noop | 4,391.51 ns |  6.144 ns |  5.447 ns | 43.59 |    0.06 | 3.0289 |      - |   12681 B |      528.38 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-analysis-filters-v2"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-analysis-filters-v2" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-analysis-filters-v2" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-analysis-filters-v2" style="max-width:960px"><canvas id="chart-bench-analysis-filters-v2" style="height:500px"></canvas></div>
<p><a href="debian-analysis-filters-v2.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


