(function(){
"use strict";

var canvas = document.querySelector("canvas[id^='chart-bench-']");
if(!canvas)return;
var suite = canvas.id.replace("chart-bench-","");
var jsonUrl = suite + ".json";

var chartJs = document.createElement("script");
chartJs.src = "https://cdn.jsdelivr.net/npm/chart.js@4";
chartJs.onload = function(){ fetch(jsonUrl).then(function(r){return r.json();}).then(render).catch(function(){}); };
document.head.appendChild(chartJs);

function render(full){
  var benchmarks = full.Benchmarks;
  if(!benchmarks||!benchmarks.length)return;

  var colors=["#4e79a7","#f28e2b","#e15759","#76b7b2","#59a14f","#edc948","#b07aa1","#ff9da7","#9c755f","#bab0ac"];

  var chartData=[];
  benchmarks.forEach(function(b){
    var label=b.MethodTitle;
    if(b.Parameters)label+=" ["+b.Parameters+"]";
    var iters=[];
    b.Measurements.forEach(function(m){if(m.IterationStage==="Result")iters.push(m.Nanoseconds);});
    chartData.push({
      label:label,
      meanNs:Math.round(b.Statistics.Mean),
      allocBytes:b.Memory.BytesAllocatedPerOperation,
      iterations:iters
    });
  });

  var labels=chartData.map(function(x){return x.label;});

  var datasets=[];

  datasets.push({
    type:"bar",
    label:"Allocated",
    data:chartData.map(function(x){return x.allocBytes;}),
    backgroundColor:colors[0]+"cc",
    yAxisID:"y",
    order:1
  });

  datasets.push({
    type:"line",
    label:"Mean time",
    data:chartData.map(function(x){return x.meanNs;}),
    borderColor:"#e15759",
    backgroundColor:"#e1575933",
    yAxisID:"y1",
    pointRadius:4,
    pointHoverRadius:6,
    order:0
  });

  chartData.forEach(function(m,i){
    datasets.push({
      type:"scatter",
      label:m.label,
      data:m.iterations.map(function(ns){return{x:m.label,y:ns};}),
      backgroundColor:colors[i%colors.length]+"55",
      yAxisID:"y1",
      pointRadius:2,
      pointHoverRadius:4,
      showLine:false,
      order:2
    });
  });

  var chart = new Chart(canvas,{
    data:{labels:labels,datasets:datasets},
    options:{
      responsive:true,
      maintainAspectRatio:false,
      interaction:{mode:"index",intersect:false},
      plugins:{
        legend:{labels:{generateLabels:function(chart){var d=chart.data.datasets;return[{text:d[0].label,fillStyle:d[0].backgroundColor,strokeStyle:d[0].borderColor,hidden:!chart.isDatasetVisible(0),datasetIndex:0},{text:d[1].label,fillStyle:d[1].backgroundColor||d[1].borderColor,strokeStyle:d[1].borderColor,hidden:!chart.isDatasetVisible(1),datasetIndex:1,pointStyle:"circle",pointStyleWidth:8}];}}},
        tooltip:{callbacks:{label:function(c){var v=c.raw.y||c.raw;if(c.dataset.yAxisID==="y")return fmtBytes(v);return fmtNs(v);}}}
      },
      scales:{
        y:{
          type:"linear",
          position:"left",
          title:{display:true,text:"Allocated"},
          ticks:{callback:fmtBytes},
          grid:{drawOnChartArea:false}
        },
        y1:makeScale("log2")
      }
    }
  });

  // Scale switcher
  var sel = document.getElementById("chart-scale-"+suite);
  if(sel){
    sel.addEventListener("change",function(){
      chart.options.scales.y1 = makeScale(this.value);
      chart.update();
    });
  }

  // Width / height sliders
  var wrap = document.getElementById("chart-wrap-"+suite);
  var widthSlider = document.getElementById("chart-width-"+suite);
  var heightSlider = document.getElementById("chart-height-"+suite);
  if(widthSlider && wrap){
    widthSlider.addEventListener("input",function(){
      wrap.style.maxWidth = this.value + "px";
      chart.resize();
    });
  }
  if(heightSlider){
    heightSlider.addEventListener("input",function(){
      canvas.style.height = this.value + "px";
      chart.resize();
    });
  }

  function makeScale(mode){
    var base = {
      type:"logarithmic",
      position:"right",
      title:{display:true,text:"Time (log\u2082)"},
      ticks:{callback:fmtNs}
    };
    if(mode==="log10"){
      base.title.text = "Time (log\u2081\u2080)";
    } else if(mode==="linear"){
      base.type = "linear";
      base.title.text = "Time (linear)";
    } else {
      // log2 — afterBuildTicks to show powers of 2
      base.afterBuildTicks = function(axis){
        var min = axis.min, max = axis.max;
        var ticks = [];
        var v = Math.pow(2, Math.floor(Math.log2(min||1)));
        while(v <= max){ ticks.push({value:v}); v *= 2; }
        axis.ticks = ticks;
      };
    }
    return base;
  }

  function fmtBytes(v){if(v>=1e9)return(v/1e9).toFixed(1)+" GB";if(v>=1e6)return(v/1e6).toFixed(1)+" MB";if(v>=1e3)return(v/1e3).toFixed(1)+" KB";return v+" B";}
  function fmtNs(v){if(v>=1e9)return(v/1e9).toFixed(2)+" s";if(v>=1e6)return(v/1e6).toFixed(2)+" ms";if(v>=1e3)return(v/1e3).toFixed(2)+" μs";return v.toFixed(0)+" ns";}
}})();

