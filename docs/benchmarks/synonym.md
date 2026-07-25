---
title: Benchmarks - Synonym
---

# Synonym

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | SynonymCount | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0        | Allocated  | Alloc Ratio |
|------------------------ |------------- |-------------- |-----------:|--------:|--------:|------:|--------:|------------:|-----------:|------------:|
| **LeanCorpus_NoSynonyms**   | **10**           | **100000**        |   **870.7 ms** | **2.23 ms** | **2.08 ms** |  **1.00** |    **0.00** |           **-** |    **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 10           | 100000        |   859.6 ms | 2.33 ms | 2.18 ms |  0.99 |    0.00 |           - |    2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 10           | 100000        | 2,167.0 ms | 1.34 ms | 1.26 ms |  2.49 |    0.01 | 144000.0000 |  577.24 MB |      252.20 |
| LuceneNet_WithSynonyms  | 10           | 100000        | 3,436.0 ms | 2.73 ms | 2.55 ms |  3.95 |    0.01 | 222000.0000 |  887.25 MB |      387.64 |
|                         |              |               |            |         |         |       |         |             |            |             |
| **LeanCorpus_NoSynonyms**   | **50**           | **100000**        |   **869.2 ms** | **1.91 ms** | **1.79 ms** |  **1.00** |    **0.00** |           **-** |    **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 50           | 100000        | 1,009.2 ms | 1.34 ms | 1.12 ms |  1.16 |    0.00 |           - |    2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 50           | 100000        | 2,179.2 ms | 1.79 ms | 1.67 ms |  2.51 |    0.01 | 144000.0000 |  577.24 MB |      252.20 |
| LuceneNet_WithSynonyms  | 50           | 100000        | 4,249.5 ms | 4.40 ms | 4.11 ms |  4.89 |    0.01 | 401000.0000 | 1599.35 MB |      698.77 |
|                         |              |               |            |         |         |       |         |             |            |             |
| **LeanCorpus_NoSynonyms**   | **200**          | **100000**        |   **868.2 ms** | **4.96 ms** | **4.64 ms** |  **1.00** |    **0.00** |           **-** |    **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 200          | 100000        |   885.6 ms | 1.38 ms | 1.15 ms |  1.02 |    0.01 |           - |    2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 200          | 100000        | 2,526.2 ms | 2.50 ms | 2.34 ms |  2.91 |    0.02 | 144000.0000 |  577.24 MB |      252.20 |
| LuceneNet_WithSynonyms  | 200          | 100000        | 5,391.4 ms | 7.26 ms | 6.79 ms |  6.21 |    0.03 | 545000.0000 | 2175.32 MB |      950.41 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-synonym"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-synonym" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-synonym" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-synonym" style="max-width:960px"><canvas id="chart-bench-synonym" style="height:500px"></canvas></div>
<p><a href="synonym.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


