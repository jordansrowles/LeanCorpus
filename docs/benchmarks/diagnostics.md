---
title: Benchmarks - diagnostics
---

# diagnostics

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |-------------- |---------:|---------:|---------:|------:|-------:|-------:|----------:|------------:|
| LeanCorpus_Search_NoHooks      | 100000        | 19.82 μs | 0.020 μs | 0.016 μs |  1.00 | 0.1831 |      - |     880 B |        1.00 |
| LeanCorpus_Search_SlowQueryLog | 100000        | 20.01 μs | 0.039 μs | 0.037 μs |  1.01 | 0.1831 |      - |     880 B |        1.00 |
| LeanCorpus_Search_Analytics    | 100000        | 20.12 μs | 0.052 μs | 0.044 μs |  1.02 | 0.2136 | 0.0305 |     936 B |        1.06 |
| LeanCorpus_Search_AllHooks     | 100000        | 20.08 μs | 0.042 μs | 0.038 μs |  1.01 | 0.2136 | 0.0305 |     936 B |        1.06 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-diagnostics"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-diagnostics" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-diagnostics" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-diagnostics" style="max-width:960px"><canvas id="chart-bench-diagnostics" style="height:500px"></canvas></div>
<p><a href="diagnostics.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


