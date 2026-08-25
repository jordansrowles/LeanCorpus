---
title: Benchmarks - Fuzzy queries
---

# Fuzzy queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                | Scenario            | DocumentCount | Mean         | Error      | StdDev     | Ratio    | RatioSD | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|---------------------- |-------------------- |-------------- |-------------:|-----------:|-----------:|---------:|--------:|---------:|---------:|-----------:|------------:|
| **LeanCorpus_FuzzyQuery** | **long-edit1-common**   | **100000**        |    **63.625 μs** |  **0.0888 μs** |  **0.0830 μs** |     **1.00** |    **0.00** |   **0.6104** |        **-** |    **2.77 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | long-edit1-common   | 100000        | 1,002.922 μs |  1.8438 μs |  1.7247 μs |    15.76 |    0.03 |  78.1250 |   1.9531 |  326.49 KB |      118.05 |
|                       |                     |               |              |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **medium-edit1-common** | **100000**        |   **151.743 μs** |  **0.1843 μs** |  **0.1724 μs** |     **1.00** |    **0.00** |   **0.9766** |        **-** |    **4.48 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | medium-edit1-common | 100000        | 1,370.996 μs |  3.7012 μs |  3.4621 μs |     9.04 |    0.02 | 242.1875 |   5.8594 |  991.89 KB |      221.19 |
|                       |                     |               |              |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **medium-edit2-common** | **100000**        |   **229.912 μs** |  **0.2785 μs** |  **0.2606 μs** |     **1.00** |    **0.00** |   **1.7090** |        **-** |    **7.49 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | medium-edit2-common | 100000        | 9,685.991 μs | 23.5341 μs | 22.0138 μs |    42.13 |    0.10 | 500.0000 | 156.2500 | 2381.52 KB |      317.87 |
|                       |                     |               |              |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **nohit-edit2**         | **100000**        |     **2.148 μs** |  **0.0015 μs** |  **0.0012 μs** |     **1.00** |    **0.00** |   **0.4730** |        **-** |    **1.94 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | nohit-edit2         | 100000        | 6,514.180 μs | 17.5678 μs | 16.4329 μs | 3,032.63 |    7.59 | 531.2500 | 210.9375 | 2511.04 KB |    1,296.02 |
|                       |                     |               |              |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **short-edit1-common**  | **100000**        |   **533.020 μs** |  **0.7841 μs** |  **0.7335 μs** |     **1.00** |    **0.00** |   **2.9297** |        **-** |   **14.12 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | short-edit1-common  | 100000        | 2,271.273 μs |  6.0406 μs |  5.6504 μs |     4.26 |    0.01 | 296.8750 |  19.5313 | 1247.14 KB |       88.34 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-fuzzy"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-fuzzy" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-fuzzy" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-fuzzy" style="max-width:960px"><canvas id="chart-bench-fuzzy" style="height:500px"></canvas></div>
<p><a href="fuzzy.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


