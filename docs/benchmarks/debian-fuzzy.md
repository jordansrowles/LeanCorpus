---
title: Benchmarks - Fuzzy queries
---

# Fuzzy queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                | Scenario            | DocumentCount | Mean          | Error      | StdDev     | Ratio    | RatioSD | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|---------------------- |-------------------- |-------------- |--------------:|-----------:|-----------:|---------:|--------:|---------:|---------:|-----------:|------------:|
| **LeanCorpus_FuzzyQuery** | **long-edit1-common**   | **100000**        |     **56.217 μs** |  **0.0318 μs** |  **0.0248 μs** |     **1.00** |    **0.00** |   **0.3662** |        **-** |     **1.6 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | long-edit1-common   | 100000        |  1,000.853 μs |  2.2884 μs |  2.0286 μs |    17.80 |    0.04 |  78.1250 |   3.9063 |  326.49 KB |      203.85 |
|                       |                     |               |               |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **medium-edit1-common** | **100000**        |    **134.595 μs** |  **0.0538 μs** |  **0.0420 μs** |     **1.00** |    **0.00** |   **0.4883** |        **-** |    **2.38 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | medium-edit1-common | 100000        |  1,365.383 μs |  3.5671 μs |  3.3367 μs |    10.14 |    0.02 | 242.1875 |   5.8594 |  991.89 KB |      416.27 |
|                       |                     |               |               |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **medium-edit2-common** | **100000**        |    **575.416 μs** |  **0.1259 μs** |  **0.1051 μs** |     **1.00** |    **0.00** |        **-** |        **-** |    **3.75 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | medium-edit2-common | 100000        | 10,335.609 μs | 18.4130 μs | 16.3227 μs |    17.96 |    0.03 | 500.0000 | 156.2500 | 2381.52 KB |      635.07 |
|                       |                     |               |               |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **nohit-edit2**         | **100000**        |      **1.403 μs** |  **0.0014 μs** |  **0.0011 μs** |     **1.00** |    **0.00** |   **0.2804** |        **-** |    **1.15 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | nohit-edit2         | 100000        |  6,477.132 μs | 15.0266 μs | 14.0559 μs | 4,617.26 |   10.29 | 523.4375 | 226.5625 | 2511.04 KB |    2,186.48 |
|                       |                     |               |               |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **short-edit1-common**  | **100000**        |    **469.129 μs** |  **0.3372 μs** |  **0.3154 μs** |     **1.00** |    **0.00** |   **1.4648** |        **-** |    **6.72 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | short-edit1-common  | 100000        |  2,243.672 μs |  6.4324 μs |  6.0169 μs |     4.78 |    0.01 | 296.8750 |  27.3438 | 1247.27 KB |      185.64 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-fuzzy"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-fuzzy" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-fuzzy" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-fuzzy" style="max-width:960px"><canvas id="chart-bench-fuzzy" style="height:500px"></canvas></div>
<p><a href="debian-fuzzy.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


