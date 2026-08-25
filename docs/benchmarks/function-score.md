---
title: Benchmarks - Function score
---

# Function score

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                        | Mode     | DocumentCount | Mean     | Error   | StdDev  | Median   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------- |-------------- |---------:|--------:|--------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_BaseTermQuery**      | **Max**      | **100000**        | **117.8 μs** | **0.16 μs** | **0.15 μs** | **117.8 μs** |  **1.00** |    **0.00** |  **0.8545** |      **-** |   **3.68 KB** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Max      | 100000        | 150.1 μs | 0.29 μs | 0.27 μs | 150.0 μs |  1.27 |    0.00 |  0.7324 |      - |   3.75 KB |        1.02 |
| LuceneNet_TermQuery           | Max      | 100000        | 191.0 μs | 0.51 μs | 0.45 μs | 191.2 μs |  1.62 |    0.00 | 18.3105 | 0.2441 |  75.72 KB |       20.58 |
| LuceneNet_FunctionScoreQuery  | Max      | 100000        | 223.6 μs | 2.37 μs | 2.22 μs | 225.4 μs |  1.90 |    0.02 | 18.5547 | 0.2441 |  77.03 KB |       20.93 |
|                               |          |               |          |         |         |          |       |         |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Multiply** | **100000**        | **115.0 μs** | **0.14 μs** | **0.13 μs** | **115.0 μs** |  **1.00** |    **0.00** |  **0.8545** |      **-** |   **3.68 KB** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Multiply | 100000        | 147.2 μs | 0.20 μs | 0.18 μs | 147.2 μs |  1.28 |    0.00 |  0.7324 |      - |   3.75 KB |        1.02 |
| LuceneNet_TermQuery           | Multiply | 100000        | 186.9 μs | 0.24 μs | 0.21 μs | 186.8 μs |  1.62 |    0.00 | 18.3105 | 0.2441 |  75.72 KB |       20.58 |
| LuceneNet_FunctionScoreQuery  | Multiply | 100000        | 226.3 μs | 1.71 μs | 1.60 μs | 227.0 μs |  1.97 |    0.01 | 18.5547 | 0.2441 |  77.03 KB |       20.93 |
|                               |          |               |          |         |         |          |       |         |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Replace**  | **100000**        | **114.5 μs** | **0.17 μs** | **0.16 μs** | **114.5 μs** |  **1.00** |    **0.00** |  **0.8545** |      **-** |   **3.68 KB** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Replace  | 100000        | 141.8 μs | 0.16 μs | 0.14 μs | 141.9 μs |  1.24 |    0.00 |  0.7324 |      - |   3.75 KB |        1.02 |
| LuceneNet_TermQuery           | Replace  | 100000        | 190.2 μs | 0.46 μs | 0.43 μs | 190.3 μs |  1.66 |    0.00 | 18.3105 | 0.2441 |  75.72 KB |       20.58 |
| LuceneNet_FunctionScoreQuery  | Replace  | 100000        | 225.4 μs | 1.66 μs | 1.56 μs | 226.2 μs |  1.97 |    0.01 | 18.5547 | 0.2441 |  77.03 KB |       20.93 |
|                               |          |               |          |         |         |          |       |         |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Sum**      | **100000**        | **116.2 μs** | **0.23 μs** | **0.22 μs** | **116.2 μs** |  **1.00** |    **0.00** |  **0.8545** |      **-** |   **3.68 KB** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Sum      | 100000        | 143.1 μs | 0.36 μs | 0.34 μs | 143.1 μs |  1.23 |    0.00 |  0.7324 |      - |   3.75 KB |        1.02 |
| LuceneNet_TermQuery           | Sum      | 100000        | 190.3 μs | 0.46 μs | 0.41 μs | 190.3 μs |  1.64 |    0.00 | 18.3105 | 0.2441 |  75.72 KB |       20.58 |
| LuceneNet_FunctionScoreQuery  | Sum      | 100000        | 229.0 μs | 1.68 μs | 1.57 μs | 229.8 μs |  1.97 |    0.01 | 18.5547 | 0.2441 |  77.03 KB |       20.93 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-function-score"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-function-score" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-function-score" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-function-score" style="max-width:960px"><canvas id="chart-bench-function-score" style="height:500px"></canvas></div>
<p><a href="function-score.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


