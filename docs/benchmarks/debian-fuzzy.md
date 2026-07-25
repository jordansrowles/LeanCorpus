---
title: Benchmarks - Fuzzy queries
---

# Fuzzy queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                | Scenario            | DocumentCount | Mean         | Error      | StdDev     | Ratio    | RatioSD | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|---------------------- |-------------------- |-------------- |-------------:|-----------:|-----------:|---------:|--------:|---------:|---------:|-----------:|------------:|
| **LeanCorpus_FuzzyQuery** | **long-edit1-common**   | **100000**        |    **58.168 μs** |  **0.5681 μs** |  **0.5314 μs** |     **1.00** |    **0.00** |   **2.0142** |        **-** |    **8.21 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | long-edit1-common   | 100000        |   772.582 μs |  0.8344 μs |  0.7397 μs |    13.28 |    0.12 |  70.3125 |   5.8594 |  287.85 KB |       35.05 |
|                       |                     |               |              |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **medium-edit1-common** | **100000**        |    **95.741 μs** |  **1.5378 μs** |  **1.4385 μs** |     **1.00** |    **0.00** |   **2.0752** |        **-** |    **8.81 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | medium-edit1-common | 100000        |   823.951 μs |  1.5413 μs |  1.4417 μs |     8.61 |    0.13 | 167.9688 |   5.8594 |  689.07 KB |       78.20 |
|                       |                     |               |              |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **medium-edit2-common** | **100000**        |   **108.984 μs** |  **2.1512 μs** |  **2.0122 μs** |     **1.00** |    **0.00** |   **2.1973** |        **-** |    **9.26 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | medium-edit2-common | 100000        | 4,439.426 μs | 17.5084 μs | 16.3774 μs |    40.75 |    0.74 | 390.6250 | 109.3750 | 1819.03 KB |      196.44 |
|                       |                     |               |              |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **nohit-edit2**         | **100000**        |     **3.481 μs** |  **0.0128 μs** |  **0.0119 μs** |     **1.00** |    **0.00** |   **1.3847** |        **-** |     **5.6 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | nohit-edit2         | 100000        | 3,605.214 μs | 14.8997 μs | 13.9372 μs | 1,035.64 |    5.18 | 449.2188 | 156.2500 | 2144.31 KB |      383.21 |
|                       |                     |               |              |            |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **short-edit1-common**  | **100000**        |   **249.825 μs** |  **3.6144 μs** |  **3.3809 μs** |     **1.00** |    **0.00** |   **2.4414** |        **-** |    **9.86 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | short-edit1-common  | 100000        | 1,267.563 μs |  4.0355 μs |  3.7748 μs |     5.07 |    0.07 | 171.8750 |   5.8594 |     705 KB |       71.51 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-fuzzy"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-fuzzy" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-fuzzy" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-fuzzy" style="max-width:960px"><canvas id="chart-bench-fuzzy" style="height:500px"></canvas></div>
<p><a href="debian-fuzzy.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


