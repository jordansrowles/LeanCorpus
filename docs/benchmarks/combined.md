---
title: Benchmarks - Combined queries
---

# Combined queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                             | MinimumShouldMatch | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0     | Gen1    | Allocated  | Alloc Ratio |
|----------------------------------- |------------------- |-------------- |-----------:|--------:|--------:|------:|---------:|--------:|-----------:|------------:|
| **LeanCorpus_CombinedFieldsQuery**     | **1**                  | **100000**        | **4,434.9 μs** | **9.43 μs** | **8.82 μs** |  **1.00** | **437.5000** | **39.0625** | **1799.41 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 1                  | 100000        |   567.4 μs | 5.71 μs | 5.34 μs |  0.13 |   6.8359 |       - |   28.71 KB |        0.02 |
| LuceneNet_BooleanQuery_MultiField  | 1                  | 100000        |   682.5 μs | 2.58 μs | 2.42 μs |  0.15 | 186.5234 |  3.9063 |  771.68 KB |        0.43 |
|                                    |                    |               |            |         |         |       |          |         |            |             |
| **LeanCorpus_CombinedFieldsQuery**     | **2**                  | **100000**        | **4,506.7 μs** | **6.65 μs** | **5.56 μs** |  **1.00** | **437.5000** | **39.0625** | **1799.41 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 2                  | 100000        |   568.5 μs | 4.00 μs | 3.54 μs |  0.13 |   6.8359 |       - |   28.72 KB |        0.02 |
| LuceneNet_BooleanQuery_MultiField  | 2                  | 100000        |   681.0 μs | 2.96 μs | 2.77 μs |  0.15 | 187.5000 |  3.9063 |  771.69 KB |        0.43 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-combined"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-combined" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-combined" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-combined" style="max-width:960px"><canvas id="chart-bench-combined" style="height:500px"></canvas></div>
<p><a href="combined.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


