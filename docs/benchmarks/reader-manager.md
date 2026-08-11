---
title: Benchmarks - reader-manager
---

# reader-manager

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| AcquireAndRelease     |  34.83 ns | 0.018 ns | 0.014 ns |  1.00 |    0.00 |      - |         - |          NA |
| AcquireLease          |  33.91 ns | 0.016 ns | 0.013 ns |  0.97 |    0.00 | 0.0210 |      88 B |          NA |
| NoOpRefresh           |  28.18 ns | 0.027 ns | 0.024 ns |  0.81 |    0.00 |      - |         - |          NA |
| PublishReplacement    | 108.43 ns | 0.783 ns | 0.733 ns |  3.11 |    0.02 | 0.0153 |      64 B |          NA |
| AcquireRetainedReader |  76.64 ns | 0.072 ns | 0.060 ns |  2.20 |    0.00 | 0.0267 |     112 B |          NA |
| GetDiagnostics        |  57.39 ns | 0.048 ns | 0.042 ns |  1.65 |    0.00 | 0.0114 |      48 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-reader-manager"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-reader-manager" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-reader-manager" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-reader-manager" style="max-width:960px"><canvas id="chart-bench-reader-manager" style="height:500px"></canvas></div>
<p><a href="reader-manager.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


