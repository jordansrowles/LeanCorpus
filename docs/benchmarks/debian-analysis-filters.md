---
title: Benchmarks - Analysis filters
---

# Analysis filters

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method           | Scenario             | Mean         | Error     | StdDev    | Ratio  | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------- |--------------------- |-------------:|----------:|----------:|-------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Apply** | **decim(...)ating [22]** |     **70.12 ns** |  **0.040 ns** |  **0.034 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | decim(...)ating [22] |  1,753.19 ns |  2.176 ns |  1.817 ns |  25.00 |    0.03 | 2.3708 |      - |    9912 B |      413.00 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **elision-mutating**     |     **84.66 ns** |  **0.117 ns** |  **0.097 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | elision-mutating     |  3,249.82 ns |  4.642 ns |  3.876 ns |  38.39 |    0.06 | 2.7313 |      - |   11432 B |      476.33 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **length-mutating**      |     **15.02 ns** |  **0.030 ns** |  **0.027 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | length-mutating      |  2,517.86 ns | 12.851 ns | 12.021 ns | 167.65 |    0.83 | 2.4986 |      - |   10448 B |      435.33 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **length-noop**          |     **15.55 ns** |  **0.042 ns** |  **0.035 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | length-noop          |  2,458.99 ns |  5.556 ns |  4.925 ns | 158.09 |    0.46 | 2.4986 |      - |   10448 B |      435.33 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **reverse-mutating**     |     **42.33 ns** |  **0.042 ns** |  **0.035 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | reverse-mutating     |  1,910.08 ns |  3.370 ns |  3.152 ns |  45.12 |    0.08 | 2.3880 |      - |    9984 B |      416.00 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **shingle-mutating**     |    **604.17 ns** |  **4.021 ns** |  **3.358 ns** |   **1.00** |    **0.00** | **0.0191** | **0.0095** |     **120 B** |        **1.00** |
| LuceneNet_Apply  | shingle-mutating     | 12,098.21 ns | 20.483 ns | 19.160 ns |  20.03 |    0.11 | 4.7302 |      - |   19816 B |      165.13 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **truncate-mutating**    |     **13.41 ns** |  **0.016 ns** |  **0.014 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | truncate-mutating    |  2,479.21 ns |  3.936 ns |  3.681 ns | 184.85 |    0.32 | 2.4948 |      - |   10433 B |      434.71 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **truncate-noop**        |     **15.07 ns** |  **0.023 ns** |  **0.020 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | truncate-noop        |  2,441.95 ns |  2.122 ns |  1.656 ns | 162.08 |    0.23 | 2.4948 |      - |   10433 B |      434.71 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **unique-mutating**      |    **164.63 ns** |  **0.183 ns** |  **0.171 ns** |   **1.00** |    **0.00** | **0.0362** |      **-** |     **152 B** |        **1.00** |
| LuceneNet_Apply  | unique-mutating      |  2,777.97 ns |  4.844 ns |  4.531 ns |  16.87 |    0.03 | 2.6283 |      - |   11000 B |       72.37 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **word-(...)ating [23]** |    **282.46 ns** |  **1.394 ns** |  **1.088 ns** |   **1.00** |    **0.00** | **0.1087** |      **-** |     **456 B** |        **1.00** |
| LuceneNet_Apply  | word-(...)ating [23] |  8,171.42 ns | 22.543 ns | 21.087 ns |  28.93 |    0.13 | 3.7842 |      - |   15880 B |       34.82 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-analysis-filters"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-analysis-filters" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-analysis-filters" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-analysis-filters" style="max-width:960px"><canvas id="chart-bench-analysis-filters" style="height:500px"></canvas></div>
<p><a href="debian-analysis-filters.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


