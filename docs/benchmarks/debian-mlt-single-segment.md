---
title: Benchmarks - mlt-single-segment
---

# mlt-single-segment

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                     | DocumentCount | Mean | Error | Ratio | RatioSD | Alloc Ratio |
|--------------------------- |-------------- |-----:|------:|------:|--------:|------------:|
| &#39;LC MLT SingleSeg Scalar&#39;  | 100000        |   NA |    NA |     ? |       ? |           ? |
| &#39;LC MLT SingleSeg WAND&#39;    | 100000        |   NA |    NA |     ? |       ? |           ? |
| &#39;Lucene.NET MLT SingleSeg&#39; | 100000        |   NA |    NA |     ? |       ? |           ? |

Benchmarks with issues:
  MoreLikeThisSingleSegmentBenchmarks.'LC MLT SingleSeg Scalar': DefaultJob [DocumentCount=100000]
  MoreLikeThisSingleSegmentBenchmarks.'LC MLT SingleSeg WAND': DefaultJob [DocumentCount=100000]
  MoreLikeThisSingleSegmentBenchmarks.'Lucene.NET MLT SingleSeg': DefaultJob [DocumentCount=100000]

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-mlt-single-segment"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-mlt-single-segment" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-mlt-single-segment" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-mlt-single-segment" style="max-width:960px"><canvas id="chart-bench-mlt-single-segment" style="height:500px"></canvas></div>
<p><a href="debian-mlt-single-segment.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


