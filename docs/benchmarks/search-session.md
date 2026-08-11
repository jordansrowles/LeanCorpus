---
title: Benchmarks - search-session
---

# search-session

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                               | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0      | Allocated  | Alloc Ratio |
|------------------------------------- |-------------- |------------:|---------:|---------:|------:|--------:|----------:|-----------:|------------:|
| DirectSearch                         | 100000        |    956.0 μs |  0.90 μs |  0.80 μs |  1.00 |    0.00 |    0.9766 |    4.34 KB |        1.00 |
| UnsignedSession_FirstPage            | 100000        |    961.9 μs |  0.96 μs |  0.85 μs |  1.01 |    0.00 |    2.9297 |    11.1 KB |        2.56 |
| SignedSession_FirstPage              | 100000        |    967.8 μs |  1.12 μs |  1.05 μs |  1.01 |    0.00 |    2.9297 |   12.16 KB |        2.80 |
| DirectSearchAfter                    | 100000        |  1,418.2 μs |  1.99 μs |  1.56 μs |  1.48 |    0.00 |    1.9531 |   15.65 KB |        3.60 |
| UnsignedSession_Continuation         | 100000        |  1,418.9 μs |  1.88 μs |  1.67 μs |  1.48 |    0.00 |    3.9063 |   20.32 KB |        4.68 |
| SignedSession_Continuation           | 100000        |  1,429.2 μs |  4.10 μs |  3.83 μs |  1.50 |    0.00 |    5.8594 |   22.56 KB |        5.20 |
| SignedSession_MultiFieldContinuation | 100000        | 16,988.2 μs | 44.31 μs | 39.28 μs | 17.77 |    0.04 | 1593.7500 | 6501.54 KB |    1,497.09 |
| OpenAndCloseSession                  | 100000        |  6,356.5 μs | 24.91 μs | 23.30 μs |  6.65 |    0.02 |  101.5625 |  412.37 KB |       94.95 |
| SessionDiagnostics                   | 100000        |  6,525.9 μs | 18.73 μs | 16.61 μs |  6.83 |    0.02 |  117.1875 |  494.29 KB |      113.82 |
| ParallelSignedContinuations          | 100000        |  2,088.3 μs | 41.65 μs | 88.76 μs |  2.18 |    0.09 |    7.8125 |   32.02 KB |        7.37 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-search-session"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-search-session" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-search-session" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-search-session" style="max-width:960px"><canvas id="chart-bench-search-session" style="height:500px"></canvas></div>
<p><a href="search-session.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


