---
title: Benchmarks - Function score
---

# Function score

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `dfecfdd` &nbsp;&middot;&nbsp; 12 July 2026 18:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                        | Mode     | DocumentCount | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------- |-------------- |----------:|----------:|----------:|----------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_BaseTermQuery**      | **Max**      | **100000**        |  **88.38 μs** |  **0.102 μs** |  **0.095 μs** |  **88.39 μs** |  **1.00** |    **0.00** |  **0.1221** |      **-** |     **720 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Max      | 100000        | 175.26 μs |  3.498 μs |  7.302 μs | 175.65 μs |  1.98 |    0.08 | 56.1523 |      - |  234447 B |      325.62 |
| LuceneNet_TermQuery           | Max      | 100000        | 135.87 μs |  1.475 μs |  1.379 μs | 136.60 μs |  1.54 |    0.02 | 13.6719 | 0.2441 |   58325 B |       81.01 |
| LuceneNet_FunctionScoreQuery  | Max      | 100000        | 164.50 μs |  1.010 μs |  0.945 μs | 164.65 μs |  1.86 |    0.01 | 13.9160 | 0.2441 |   59356 B |       82.44 |
|                               |          |               |           |           |           |           |       |         |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Multiply** | **100000**        |  **91.58 μs** |  **0.369 μs** |  **0.345 μs** |  **91.69 μs** |  **1.00** |    **0.00** |  **0.1221** |      **-** |     **720 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Multiply | 100000        | 176.95 μs |  3.508 μs |  8.670 μs | 178.69 μs |  1.93 |    0.09 | 56.1523 |      - |  234427 B |      325.59 |
| LuceneNet_TermQuery           | Multiply | 100000        | 132.85 μs |  0.259 μs |  0.230 μs | 132.76 μs |  1.45 |    0.01 | 13.6719 | 0.2441 |   58325 B |       81.01 |
| LuceneNet_FunctionScoreQuery  | Multiply | 100000        | 163.49 μs |  0.721 μs |  0.674 μs | 163.66 μs |  1.79 |    0.01 | 13.9160 | 0.2441 |   59356 B |       82.44 |
|                               |          |               |           |           |           |           |       |         |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Replace**  | **100000**        |  **90.73 μs** |  **0.320 μs** |  **0.300 μs** |  **90.77 μs** |  **1.00** |    **0.00** |  **0.1221** |      **-** |     **720 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Replace  | 100000        | 160.66 μs |  4.437 μs | 13.084 μs | 159.04 μs |  1.77 |    0.14 | 56.1523 |      - |  234426 B |      325.59 |
| LuceneNet_TermQuery           | Replace  | 100000        | 132.88 μs |  1.056 μs |  0.988 μs | 133.25 μs |  1.46 |    0.01 | 13.6719 | 0.2441 |   58325 B |       81.01 |
| LuceneNet_FunctionScoreQuery  | Replace  | 100000        | 159.85 μs |  1.113 μs |  0.987 μs | 159.55 μs |  1.76 |    0.01 | 13.9160 | 0.2441 |   59356 B |       82.44 |
|                               |          |               |           |           |           |           |       |         |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Sum**      | **100000**        |  **91.76 μs** |  **0.773 μs** |  **0.723 μs** |  **91.77 μs** |  **1.00** |    **0.00** |  **0.1221** |      **-** |     **720 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Sum      | 100000        | 192.65 μs | 11.014 μs | 31.244 μs | 181.34 μs |  2.10 |    0.34 | 56.1523 |      - |  234445 B |      325.62 |
| LuceneNet_TermQuery           | Sum      | 100000        | 134.60 μs |  0.125 μs |  0.098 μs | 134.62 μs |  1.47 |    0.01 | 13.6719 | 0.2441 |   58325 B |       81.01 |
| LuceneNet_FunctionScoreQuery  | Sum      | 100000        | 157.48 μs |  0.520 μs |  0.487 μs | 157.53 μs |  1.72 |    0.01 | 13.9160 | 0.2441 |   59356 B |       82.44 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-function-score"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-function-score" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-function-score" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-function-score" style="max-width:960px"><canvas id="chart-bench-function-score" style="height:500px"></canvas></div>
<p><a href="debian-function-score.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


