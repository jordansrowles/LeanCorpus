---
title: Benchmarks - Collapse and facet
---

# Collapse and facet

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `dfecfdd` &nbsp;&middot;&nbsp; 12 July 2026 18:30 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean         | Error     | StdDev    | Ratio  | RatioSD | Gen0      | Gen1     | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |-------------:|----------:|----------:|-------:|--------:|----------:|---------:|----------:|------------:|
| LeanCorpus_BaseSearch                  | 100000        |     87.75 μs |  0.136 μs |  0.120 μs |   1.00 |    0.00 |    0.1221 |        - |     616 B |        1.00 |
| LeanCorpus_SearchWithCollapse          | 100000        |    350.86 μs |  0.394 μs |  0.308 μs |   4.00 |    0.01 |    0.4883 |        - |    3936 B |        6.39 |
| LeanCorpus_SearchWithFacets            | 100000        |    467.29 μs |  0.670 μs |  0.627 μs |   5.33 |    0.01 |   57.1289 |  11.2305 |  239280 B |      388.44 |
| LeanCorpus_SearchWithCollapseAndFacets | 100000        |    843.61 μs |  1.147 μs |  1.073 μs |   9.61 |    0.02 |   57.6172 |  13.6719 |  243216 B |      394.83 |
| LuceneNet_TermQuery                    | 100000        |     98.35 μs |  0.435 μs |  0.407 μs |   1.12 |    0.00 |    5.2490 |   0.1221 |   22375 B |       36.32 |
| LuceneNet_SearchWithCollapse           | 100000        | 16,371.48 μs | 39.503 μs | 35.018 μs | 186.57 |    0.46 | 1000.0000 | 406.2500 | 5414751 B |    8,790.18 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-collapse-facet"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-collapse-facet" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-collapse-facet" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-collapse-facet" style="max-width:960px"><canvas id="chart-bench-collapse-facet" style="height:500px"></canvas></div>
<p><a href="debian-collapse-facet.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


