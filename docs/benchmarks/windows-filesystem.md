---
title: Benchmarks - windows-filesystem
---

# windows-filesystem

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f2a3960` &nbsp;&middot;&nbsp; 22 August 2026 07:32 UTC &nbsp;&middot;&nbsp; 1,000 docs

| Method               | DurableCommits | UseCompoundFile | DocumentCount | Mean     | Error    | StdDev   | Gen0      | Gen1      | Allocated |
|--------------------- |--------------- |---------------- |-------------- |---------:|---------:|---------:|----------:|----------:|----------:|
| RepeatedSmallCommits | True           | False           | 1000          | 553.0 ms | 624.9 ms | 34.25 ms | 2000.0000 | 1000.0000 |  18.83 MB |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-windows-filesystem"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-windows-filesystem" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-windows-filesystem" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-windows-filesystem" style="max-width:960px"><canvas id="chart-bench-windows-filesystem" style="height:500px"></canvas></div>
<p><a href="windows-filesystem.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


