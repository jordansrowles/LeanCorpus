---
title: Benchmarks - fst-lookup
---

# fst-lookup

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 7 August 2026 08:49 UTC &nbsp;&middot;&nbsp; 20,000 docs

| Method                              | TermCount | Mean        | Error       | StdDev      | Median      | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------------ |---------- |------------:|------------:|------------:|------------:|------:|--------:|----------:|------------:|
| **&#39;FST TryGetOutput&#39;**                  | **1000**      |  **1,336.7 μs** |    **592.2 μs** |    **153.8 μs** |  **1,284.6 μs** |  **1.00** |    **0.00** |         **-** |          **NA** |
| &#39;FST EnumeratePrefix&#39;               | 1000      |    286.9 μs |    692.6 μs |    179.9 μs |    214.6 μs |  0.22 |    0.13 |   40720 B |          NA |
| &#39;FST IntersectAutomaton (wildcard)&#39; | 1000      |  1,598.6 μs | 10,845.5 μs |  2,816.6 μs |    348.4 μs |  1.21 |    1.95 |   41432 B |          NA |
|                                     |           |             |             |             |             |       |         |           |             |
| **&#39;FST TryGetOutput&#39;**                  | **10000**     |  **6,371.8 μs** | **26,078.1 μs** |  **6,772.4 μs** |  **3,355.2 μs** |  **1.00** |    **0.00** |         **-** |          **NA** |
| &#39;FST EnumeratePrefix&#39;               | 10000     |  2,669.1 μs | 10,311.6 μs |  2,677.9 μs |  1,481.5 μs |  0.67 |    0.71 |  400720 B |          NA |
| &#39;FST IntersectAutomaton (wildcard)&#39; | 10000     |  3,923.4 μs | 14,342.7 μs |  3,724.7 μs |  2,285.7 μs |  0.98 |    0.99 |  401432 B |          NA |
|                                     |           |             |             |             |             |       |         |           |             |
| **&#39;FST TryGetOutput&#39;**                  | **100000**    | **22,092.2 μs** | **47,607.7 μs** | **12,363.6 μs** | **16,631.5 μs** |  **1.00** |    **0.00** |         **-** |          **NA** |
| &#39;FST EnumeratePrefix&#39;               | 100000    | 14,917.1 μs |  8,016.0 μs |  2,081.7 μs | 13,985.4 μs |  0.79 |    0.25 | 4000720 B |          NA |
| &#39;FST IntersectAutomaton (wildcard)&#39; | 100000    | 23,020.9 μs | 16,539.6 μs |  4,295.3 μs | 21,235.1 μs |  1.22 |    0.42 | 4001432 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-fst-lookup"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-fst-lookup" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-fst-lookup" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-fst-lookup" style="max-width:960px"><canvas id="chart-bench-fst-lookup" style="height:500px"></canvas></div>
<p><a href="fst-lookup.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


