---
title: Benchmarks - Analysis filters
---

# Analysis filters

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method           | Scenario             | Mean         | Error     | StdDev    | Ratio  | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------- |--------------------- |-------------:|----------:|----------:|-------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Apply** | **decim(...)ating [22]** |     **70.99 ns** |  **0.039 ns** |  **0.033 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | decim(...)ating [22] |  1,832.77 ns |  3.188 ns |  2.982 ns |  25.82 |    0.04 | 2.3708 |      - |    9912 B |      413.00 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **elision-mutating**     |     **84.51 ns** |  **0.058 ns** |  **0.049 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | elision-mutating     |  3,240.93 ns |  5.752 ns |  5.381 ns |  38.35 |    0.07 | 2.7313 |      - |   11432 B |      476.33 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **length-mutating**      |     **14.99 ns** |  **0.011 ns** |  **0.010 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | length-mutating      |  2,505.64 ns |  3.519 ns |  3.292 ns | 167.13 |    0.24 | 2.4986 |      - |   10448 B |      435.33 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **length-noop**          |     **15.68 ns** |  **0.033 ns** |  **0.030 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | length-noop          |  2,436.79 ns |  2.014 ns |  1.573 ns | 155.42 |    0.30 | 2.4986 |      - |   10448 B |      435.33 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **reverse-mutating**     |     **43.53 ns** |  **0.069 ns** |  **0.057 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | reverse-mutating     |  1,889.26 ns |  4.793 ns |  4.483 ns |  43.41 |    0.11 | 2.3880 |      - |    9984 B |      416.00 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **shingle-mutating**     |    **601.17 ns** |  **3.398 ns** |  **3.012 ns** |   **1.00** |    **0.00** | **0.0191** | **0.0095** |     **120 B** |        **1.00** |
| LuceneNet_Apply  | shingle-mutating     | 12,163.15 ns | 26.489 ns | 24.778 ns |  20.23 |    0.11 | 4.7302 |      - |   19816 B |      165.13 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **truncate-mutating**    |     **13.14 ns** |  **0.013 ns** |  **0.013 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | truncate-mutating    |  2,489.53 ns |  4.148 ns |  3.677 ns | 189.39 |    0.32 | 2.4948 |      - |   10433 B |      434.71 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **truncate-noop**        |     **15.09 ns** |  **0.008 ns** |  **0.006 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | truncate-noop        |  2,420.81 ns | 10.351 ns |  8.644 ns | 160.42 |    0.56 | 2.4948 |      - |   10433 B |      434.71 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **unique-mutating**      |    **163.00 ns** |  **0.239 ns** |  **0.200 ns** |   **1.00** |    **0.00** | **0.0362** |      **-** |     **152 B** |        **1.00** |
| LuceneNet_Apply  | unique-mutating      |  2,862.53 ns |  5.106 ns |  4.776 ns |  17.56 |    0.04 | 2.6283 |      - |   11000 B |       72.37 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **word-(...)ating [23]** |    **285.30 ns** |  **4.768 ns** |  **4.460 ns** |   **1.00** |    **0.00** | **0.1087** |      **-** |     **456 B** |        **1.00** |
| LuceneNet_Apply  | word-(...)ating [23] |  8,207.05 ns | 20.441 ns | 19.121 ns |  28.77 |    0.44 | 3.7842 |      - |   15880 B |       34.82 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-analysis-filters"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-analysis-filters" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-analysis-filters" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-analysis-filters" style="max-width:960px"><canvas id="chart-bench-analysis-filters" style="height:500px"></canvas></div>
<p><a href="debian-analysis-filters.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


