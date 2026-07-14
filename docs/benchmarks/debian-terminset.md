---
title: Benchmarks - Term in set
---

# Term in set

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `23d347f` &nbsp;&middot;&nbsp; 12 July 2026 18:57 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | SetSize | DocumentCount | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|------------------------------- |-------- |-------------- |-----------:|----------:|----------:|------:|--------:|---------:|---------:|-----------:|------------:|
| **LeanCorpus_TermInSetQuery**      | **5**       | **100000**        |   **437.7 μs** |   **2.05 μs** |   **1.92 μs** |  **1.00** |    **0.00** |   **1.9531** |        **-** |    **8.94 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 5       | 100000        |   769.1 μs |   8.36 μs |   7.82 μs |  1.76 |    0.02 |   4.8828 |        - |   23.27 KB |        2.60 |
| LuceneNet_BooleanQuery_Should  | 5       | 100000        | 1,105.5 μs |   1.00 μs |   0.78 μs |  2.53 |    0.01 |  85.9375 |  15.6250 |  353.74 KB |       39.58 |
|                                |         |               |            |           |           |       |         |          |          |            |             |
| **LeanCorpus_TermInSetQuery**      | **20**      | **100000**        |   **785.1 μs** |   **8.04 μs** |   **7.13 μs** |  **1.00** |    **0.00** |   **2.9297** |        **-** |   **14.26 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 20      | 100000        | 1,892.4 μs |  31.53 μs |  29.49 μs |  2.41 |    0.04 |  19.5313 |        - |   75.49 KB |        5.29 |
| LuceneNet_BooleanQuery_Should  | 20      | 100000        | 2,662.6 μs |  13.22 μs |  12.36 μs |  3.39 |    0.03 | 187.5000 |  23.4375 |  770.31 KB |       54.02 |
|                                |         |               |            |           |           |       |         |          |          |            |             |
| **LeanCorpus_TermInSetQuery**      | **100**     | **100000**        | **1,474.7 μs** |  **29.08 μs** |  **57.39 μs** |  **1.00** |    **0.00** |   **9.7656** |        **-** |   **46.05 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 100     | 100000        | 5,604.8 μs | 108.50 μs | 165.69 μs |  3.81 |    0.18 | 148.4375 | 117.1875 |  850.55 KB |       18.47 |
| LuceneNet_BooleanQuery_Should  | 100     | 100000        | 5,921.5 μs |   9.45 μs |   8.38 μs |  4.02 |    0.15 | 585.9375 | 218.7500 | 2779.76 KB |       60.36 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-terminset"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-terminset" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-terminset" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-terminset" style="max-width:960px"><canvas id="chart-bench-terminset" style="height:500px"></canvas></div>
<p><a href="debian-terminset.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


