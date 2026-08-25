---
title: Benchmarks - ranking-pipeline
---

# ranking-pipeline

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | DocumentCount | Mean       | Error     | StdDev    | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|-------------------------- |-------------- |-----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| DirectSearch              | 100000        | 184.049 μs | 0.1249 μs | 0.0976 μs |  1.00 |  3.6621 |      - |  15.27 KB |        1.00 |
| EmptyProfile              | 100000        | 202.222 μs | 0.5175 μs | 0.4841 μs |  1.10 |  4.3945 |      - |  18.14 KB |        1.19 |
| RulesFiltersScoresAndPins | 100000        | 219.424 μs | 1.0996 μs | 1.0286 μs |  1.19 | 25.1465 |      - | 102.13 KB |        6.69 |
| ScoreFunctionPipeline     | 100000        | 242.305 μs | 0.2944 μs | 0.2754 μs |  1.32 |  6.5918 | 0.2441 |   27.4 KB |        1.79 |
| QueryRescorerPipeline     | 100000        | 551.459 μs | 1.3387 μs | 1.2522 μs |  3.00 |  5.8594 |      - |  27.65 KB |        1.81 |
| CachedProfileHit          | 100000        |   2.481 μs | 0.0048 μs | 0.0043 μs |  0.01 |  0.2899 |      - |   1.19 KB |        0.08 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-ranking-pipeline"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-ranking-pipeline" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-ranking-pipeline" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-ranking-pipeline" style="max-width:960px"><canvas id="chart-bench-ranking-pipeline" style="height:500px"></canvas></div>
<p><a href="ranking-pipeline.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


