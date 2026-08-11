---
title: Benchmarks - ranking-evaluation
---

# ranking-evaluation

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                            | CandidateCount | TopN | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0    | Gen1    | Allocated | Alloc Ratio |
|---------------------------------- |--------------- |----- |-----------:|----------:|----------:|------:|--------:|--------:|--------:|----------:|------------:|
| **CalculateMetrics**                  | **25**             | **10**   |   **9.198 μs** | **0.0177 μs** | **0.0165 μs** |  **1.00** |    **0.00** |  **3.0975** |       **-** |  **12.71 KB** |        **1.00** |
| SelectMmr                         | 25             | 10   |   6.264 μs | 0.0062 μs | 0.0058 μs |  0.68 |    0.00 |  0.7477 |       - |   3.08 KB |        0.24 |
| SelectMmr_WithMissingSimilarities | 25             | 10   |   6.130 μs | 0.0057 μs | 0.0047 μs |  0.67 |    0.00 |  0.7477 |       - |   3.08 KB |        0.24 |
|                                   |                |      |            |           |           |       |         |         |         |           |             |
| **CalculateMetrics**                  | **25**             | **25**   |  **11.151 μs** | **0.0112 μs** | **0.0105 μs** |  **1.00** |    **0.00** |  **3.1281** |       **-** |  **12.83 KB** |        **1.00** |
| SelectMmr                         | 25             | 25   |  15.483 μs | 0.0160 μs | 0.0142 μs |  1.39 |    0.00 |  0.9155 |       - |   3.78 KB |        0.29 |
| SelectMmr_WithMissingSimilarities | 25             | 25   |  11.232 μs | 0.0153 μs | 0.0143 μs |  1.01 |    0.00 |  0.8392 |       - |   3.43 KB |        0.27 |
|                                   |                |      |            |           |           |       |         |         |         |           |             |
| **CalculateMetrics**                  | **100**            | **10**   |  **30.014 μs** | **0.0346 μs** | **0.0324 μs** |  **1.00** |    **0.00** | **12.8174** |       **-** |  **52.35 KB** |        **1.00** |
| SelectMmr                         | 100            | 10   |  28.528 μs | 0.0286 μs | 0.0267 μs |  0.95 |    0.00 |  2.3499 |       - |    9.7 KB |        0.19 |
| SelectMmr_WithMissingSimilarities | 100            | 10   |  27.378 μs | 0.0413 μs | 0.0367 μs |  0.91 |    0.00 |  2.3499 |       - |    9.7 KB |        0.19 |
|                                   |                |      |            |           |           |       |         |         |         |           |             |
| **CalculateMetrics**                  | **100**            | **25**   |  **35.098 μs** | **0.0505 μs** | **0.0473 μs** |  **1.00** |    **0.00** | **12.8174** |  **0.0610** |  **52.47 KB** |        **1.00** |
| SelectMmr                         | 100            | 25   | 133.282 μs | 0.1786 μs | 0.1583 μs |  3.80 |    0.01 |  2.4414 |       - |   10.4 KB |        0.20 |
| SelectMmr_WithMissingSimilarities | 100            | 25   | 129.460 μs | 0.1043 μs | 0.0924 μs |  3.69 |    0.01 |  2.4414 |       - |   10.4 KB |        0.20 |
|                                   |                |      |            |           |           |       |         |         |         |           |             |
| **CalculateMetrics**                  | **500**            | **10**   | **159.728 μs** | **1.6750 μs** | **1.5668 μs** |  **1.00** |    **0.00** | **59.8145** | **19.7754** | **245.54 KB** |        **1.00** |
| SelectMmr                         | 500            | 10   | 175.617 μs | 0.3897 μs | 0.3645 μs |  1.10 |    0.01 | 10.0098 |  0.4883 |  41.84 KB |        0.17 |
| SelectMmr_WithMissingSimilarities | 500            | 10   | 169.258 μs | 0.3682 μs | 0.3444 μs |  1.06 |    0.01 | 10.0098 |  0.4883 |  41.84 KB |        0.17 |
|                                   |                |      |            |           |           |       |         |         |         |           |             |
| **CalculateMetrics**                  | **500**            | **25**   | **163.079 μs** | **1.2021 μs** | **1.0656 μs** |  **1.00** |    **0.00** | **56.1523** | **15.1367** | **245.66 KB** |        **1.00** |
| SelectMmr                         | 500            | 25   | 893.988 μs | 1.0346 μs | 0.9172 μs |  5.48 |    0.04 |  9.7656 |       - |  42.54 KB |        0.17 |
| SelectMmr_WithMissingSimilarities | 500            | 25   | 866.368 μs | 0.9313 μs | 0.8255 μs |  5.31 |    0.03 |  9.7656 |       - |  42.54 KB |        0.17 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-ranking-evaluation"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-ranking-evaluation" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-ranking-evaluation" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-ranking-evaluation" style="max-width:960px"><canvas id="chart-bench-ranking-evaluation" style="height:500px"></canvas></div>
<p><a href="ranking-evaluation.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


