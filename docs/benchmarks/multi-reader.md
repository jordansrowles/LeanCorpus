---
title: Benchmarks - multi-reader
---

# multi-reader

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | ReaderCount | DocumentCount | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1     | Gen2    | Allocated  | Alloc Ratio |
|------------------------ |------------ |-------------- |------------:|----------:|----------:|------:|--------:|----------:|---------:|--------:|-----------:|------------:|
| **OpenMultiReader**         | **1**           | **100000**        | **14,826.9 μs** |  **36.71 μs** |  **34.34 μs** | **15.09** |    **0.03** | **1265.6250** | **609.3750** |       **-** |  **6572511 B** |   **10,810.05** |
| SingleIndexSearch       | 1           | 100000        |    982.7 μs |   0.58 μs |   0.51 μs |  1.00 |    0.00 |         - |        - |       - |      608 B |        1.00 |
| FederatedSearch         | 1           | 100000        |  1,100.7 μs |   1.86 μs |   1.65 μs |  1.12 |    0.00 |         - |        - |       - |     3736 B |        6.14 |
| SingleIndexContinuation | 1           | 100000        |  1,215.0 μs |   1.29 μs |   1.21 μs |  1.24 |    0.00 |         - |        - |       - |      664 B |        1.09 |
| FederatedContinuation   | 1           | 100000        | 48,108.3 μs | 137.99 μs | 115.22 μs | 48.95 |    0.12 | 1363.6364 | 636.3636 |       - | 12498136 B |   20,556.14 |
| FederatedFacets         | 1           | 100000        | 39,515.5 μs |  62.22 μs |  58.20 μs | 40.21 |    0.06 |  846.1538 |  76.9231 | 76.9231 | 11292975 B |   18,573.97 |
| BuildGlobalOrdinalMap   | 1           | 100000        |    201.2 μs |   0.39 μs |   0.35 μs |  0.20 |    0.00 |   11.9629 |   1.2207 |       - |    50864 B |       83.66 |
|                         |             |               |             |           |           |       |         |           |          |         |            |             |
| **OpenMultiReader**         | **4**           | **100000**        | **15,498.0 μs** |  **18.70 μs** |  **16.57 μs** | **15.77** |    **0.02** | **1187.5000** | **921.8750** |       **-** |  **6662894 B** |   **10,958.71** |
| SingleIndexSearch       | 4           | 100000        |    982.5 μs |   0.28 μs |   0.24 μs |  1.00 |    0.00 |         - |        - |       - |      608 B |        1.00 |
| FederatedSearch         | 4           | 100000        |  1,111.3 μs |   1.64 μs |   1.37 μs |  1.13 |    0.00 |    1.9531 |        - |       - |    13576 B |       22.33 |
| SingleIndexContinuation | 4           | 100000        |  1,186.6 μs |   1.70 μs |   1.50 μs |  1.21 |    0.00 |         - |        - |       - |      664 B |        1.09 |
| FederatedContinuation   | 4           | 100000        | 45,580.1 μs | 165.98 μs | 138.60 μs | 46.39 |    0.14 | 1363.6364 | 636.3636 |       - | 12498656 B |   20,557.00 |
| FederatedFacets         | 4           | 100000        | 32,364.3 μs |  45.10 μs |  37.66 μs | 32.94 |    0.04 | 1062.5000 | 125.0000 | 62.5000 | 10971576 B |   18,045.36 |
| BuildGlobalOrdinalMap   | 4           | 100000        |    196.7 μs |   0.26 μs |   0.23 μs |  0.20 |    0.00 |   13.9160 |        - |       - |    58856 B |       96.80 |
|                         |             |               |             |           |           |       |         |           |          |         |            |             |
| **OpenMultiReader**         | **16**          | **100000**        | **18,198.6 μs** |  **74.54 μs** |  **69.72 μs** | **18.51** |    **0.07** | **1375.0000** | **843.7500** |       **-** |  **7805067 B** |   **12,837.28** |
| SingleIndexSearch       | 16          | 100000        |    983.1 μs |   0.56 μs |   0.47 μs |  1.00 |    0.00 |         - |        - |       - |      608 B |        1.00 |
| FederatedSearch         | 16          | 100000        |    993.3 μs |   2.24 μs |   2.09 μs |  1.01 |    0.00 |   11.7188 |        - |       - |    52760 B |       86.78 |
| SingleIndexContinuation | 16          | 100000        |  1,214.0 μs |   0.62 μs |   0.52 μs |  1.23 |    0.00 |         - |        - |       - |      664 B |        1.09 |
| FederatedContinuation   | 16          | 100000        | 48,502.3 μs | 123.81 μs | 115.81 μs | 49.34 |    0.12 | 1727.2727 | 909.0909 | 90.9091 | 12500993 B |   20,560.84 |
| FederatedFacets         | 16          | 100000        | 30,495.7 μs |  49.86 μs |  46.64 μs | 31.02 |    0.05 | 2156.2500 | 687.5000 |       - | 10740856 B |   17,665.88 |
| BuildGlobalOrdinalMap   | 16          | 100000        |    252.1 μs |   0.19 μs |   0.15 μs |  0.26 |    0.00 |   22.9492 |        - |       - |    96344 B |      158.46 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-multi-reader"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-multi-reader" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-multi-reader" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-multi-reader" style="max-width:960px"><canvas id="chart-bench-multi-reader" style="height:500px"></canvas></div>
<p><a href="multi-reader.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


