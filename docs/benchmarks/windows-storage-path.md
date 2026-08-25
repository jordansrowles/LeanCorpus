---
title: Benchmarks - windows-storage-path
---

# windows-storage-path

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `7a2a3ac` &nbsp;&middot;&nbsp; 23 August 2026 10:01 UTC &nbsp;&middot;&nbsp; 20,000 docs

| Method                             | Mean     | Error | Ratio | Allocated | Alloc Ratio |
|----------------------------------- |---------:|------:|------:|----------:|------------:|
| PrimitiveVarInt_PerReadDrain       | 6.367 ms |    NA |  1.00 |         - |          NA |
| PrimitiveVarInt_ScopedDecoderLease | 2.805 ms |    NA |  0.44 |         - |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-windows-storage-path"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-windows-storage-path" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-windows-storage-path" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-windows-storage-path" style="max-width:960px"><canvas id="chart-bench-windows-storage-path" style="height:500px"></canvas></div>
<p><a href="windows-storage-path.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


