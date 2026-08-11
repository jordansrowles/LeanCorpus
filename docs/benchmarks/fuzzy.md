---
title: Benchmarks - Fuzzy queries
---

# Fuzzy queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                | Scenario            | DocumentCount | Mean          | Error      | StdDev     | Ratio    | RatioSD | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|---------------------- |-------------------- |-------------- |--------------:|-----------:|-----------:|---------:|--------:|---------:|---------:|-----------:|------------:|
| **LeanCorpus_FuzzyQuery** | **long-edit1-common**   | **100000**        |     **60.061 μs** |  **0.0824 μs** |  **0.0730 μs** |     **1.00** |    **0.00** |   **0.3662** |        **-** |    **1.55 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | long-edit1-common   | 100000        |  1,001.880 μs |  2.0664 μs |  1.9329 μs |    16.68 |    0.04 |  78.1250 |   1.9531 |  326.49 KB |      211.06 |
|                       |                     |               |               |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **medium-edit1-common** | **100000**        |    **146.524 μs** |  **0.1555 μs** |  **0.1455 μs** |     **1.00** |    **0.00** |   **0.4883** |        **-** |    **2.17 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | medium-edit1-common | 100000        |  1,370.434 μs |  3.0805 μs |  2.7308 μs |     9.35 |    0.02 | 242.1875 |   5.8594 |  991.89 KB |      456.70 |
|                       |                     |               |               |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **medium-edit2-common** | **100000**        |    **216.999 μs** |  **0.2144 μs** |  **0.1901 μs** |     **1.00** |    **0.00** |   **0.7324** |        **-** |    **3.27 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | medium-edit2-common | 100000        | 10,378.525 μs | 17.8961 μs | 16.7400 μs |    47.83 |    0.08 | 500.0000 | 156.2500 | 2381.52 KB |      729.27 |
|                       |                     |               |               |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **nohit-edit2**         | **100000**        |      **1.376 μs** |  **0.0021 μs** |  **0.0017 μs** |     **1.00** |    **0.00** |   **0.2823** |        **-** |    **1.16 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | nohit-edit2         | 100000        |  6,469.398 μs | 16.0442 μs | 15.0078 μs | 4,702.15 |   12.00 | 523.4375 | 226.5625 | 2511.04 KB |    2,171.71 |
|                       |                     |               |               |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **short-edit1-common**  | **100000**        |    **509.667 μs** |  **0.3959 μs** |  **0.3306 μs** |     **1.00** |    **0.00** |   **0.9766** |        **-** |    **5.63 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | short-edit1-common  | 100000        |  2,249.980 μs | 10.0976 μs |  9.4453 μs |     4.41 |    0.02 | 296.8750 |  27.3438 | 1247.27 KB |      221.74 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-fuzzy"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-fuzzy" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-fuzzy" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-fuzzy" style="max-width:960px"><canvas id="chart-bench-fuzzy" style="height:500px"></canvas></div>
<p><a href="fuzzy.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


