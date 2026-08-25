---
title: Architecture Decision Records
_description: Recorded LeanCorpus architecture decisions and their status.
---

# Architecture Decision Records

<div class="table-responsive">
<table class="table table-sm table-striped adr-index-table">
<thead>
<tr><th>ADR</th><th>Date</th><th>Status</th><th>Previous</th><th>Decision</th><th>Next</th></tr>
</thead>
<tbody>
<tr><td><a href="ADR001-span-body-encoding.md">001</a></td><td>2026-06-16</td><td>Accepted</td><td></td><td><a href="ADR001-span-body-encoding.md">Span-based body encoding for segment serialisation</a></td><td></td></tr>
<tr><td><a href="ADR002-single-simd-path.md">002</a></td><td>2026-06-16</td><td>Accepted</td><td></td><td><a href="ADR002-single-simd-path.md">Single auto-vectorised SIMD path</a></td><td></td></tr>
<tr><td><a href="ADR003-hnsw-frozen-sorted-arrays.md">003</a></td><td>2026-06-16</td><td>Accepted</td><td></td><td><a href="ADR003-hnsw-frozen-sorted-arrays.md">Sorted parallel arrays for HNSW frozen adjacency</a></td><td></td></tr>
<tr><td><a href="ADR004-concurrentdictionary-cache-pattern.md">004</a></td><td>2026-06-16</td><td>Accepted</td><td></td><td><a href="ADR004-concurrentdictionary-cache-pattern.md">ConcurrentDictionary with generation-swap eviction for read-heavy caches</a></td><td></td></tr>
<tr><td><a href="ADR005-dwpt-segment-flush.md">005</a></td><td>2026-06-16</td><td>Accepted</td><td></td><td><a href="ADR005-dwpt-segment-flush.md">Each DWPT flushes its own segment</a></td><td></td></tr>
<tr><td><a href="ADR006-stryker-deferred.md">006</a></td><td>2026-06-17</td><td>Accepted</td><td></td><td><a href="ADR006-stryker-deferred.md">Defer Stryker.NET mutation testing until upstream bug is fixed</a></td><td></td></tr>
<tr><td><a href="ADR007-merge-must-not-block-commit.md">007</a></td><td>2026-06-18</td><td>Accepted</td><td></td><td><a href="ADR007-merge-must-not-block-commit.md">Background merges must never block Commit</a></td><td></td></tr>
<tr><td><a href="ADR008-stored-fields-v2-streaming.md">008</a></td><td>2026-07-09</td><td>Superseded</td><td></td><td><a href="ADR008-stored-fields-v2-streaming.md">Streaming codec formats bypass the CodecKit envelope</a></td><td><a href="ADR009-codeckit-trailer-streaming.md">009</a></td></tr>
<tr><td><a href="ADR009-codeckit-trailer-streaming.md">009</a></td><td>2026-07-11</td><td>Superseded</td><td><a href="ADR008-stored-fields-v2-streaming.md">008</a></td><td><a href="ADR009-codeckit-trailer-streaming.md">CodecKit trailer format replaces ADR008 custom headers</a></td><td><a href="ADR026-canonical-binary-file-frame.md">026</a></td></tr>
<tr><td><a href="ADR010-close-before-rename-migration.md">010</a></td><td>2026-07-14</td><td>Accepted</td><td></td><td><a href="ADR010-close-before-rename-migration.md">IndexOutput must be disposed before File.Move on Windows</a></td><td></td></tr>
<tr><td><a href="ADR011-lazy-segment-reader-lifetimes.md">011</a></td><td>2026-07-21</td><td>Accepted</td><td></td><td><a href="ADR011-lazy-segment-reader-lifetimes.md">Lazy segment readers use bounded leases and process-wide file lifetimes</a></td><td></td></tr>
<tr><td><a href="ADR012-parallel-search-opt-in.md">012</a></td><td>2026-07-24</td><td>Accepted</td><td></td><td><a href="ADR012-parallel-search-opt-in.md">Parallel segment search is opt-in</a></td><td></td></tr>
<tr><td><a href="ADR013-query-extension-pipeline.md">013</a></td><td>2026-07-27</td><td>Accepted</td><td></td><td><a href="ADR013-query-extension-pipeline.md">Custom queries extend the tuned execution pipeline</a></td><td></td></tr>
<tr><td><a href="ADR014-japanese-language-codec.md">014</a></td><td>2026-07-27</td><td>Accepted</td><td></td><td><a href="ADR014-japanese-language-codec.md">Japanese dictionaries use a LeanCorpus language codec</a></td><td></td></tr>
<tr><td><a href="ADR015-bounded-second-stage-search.md">015</a></td><td>2026-07-27</td><td>Accepted</td><td></td><td><a href="ADR015-bounded-second-stage-search.md">Pagination and rescoring use bounded collector strategies</a></td><td></td></tr>
<tr><td><a href="ADR016-experimental-hybrid-retrieval-ship-gates.md">016</a></td><td>2026-07-28</td><td>Accepted</td><td></td><td><a href="ADR016-experimental-hybrid-retrieval-ship-gates.md">Experimental hybrid retrieval requires measured ship gates</a></td><td></td></tr>
<tr><td><a href="ADR017-reject-matryoshka-prefix-retrieval.md">017</a></td><td>2026-07-30</td><td>Accepted</td><td></td><td><a href="ADR017-reject-matryoshka-prefix-retrieval.md">Reject Matryoshka prefix retrieval</a></td><td></td></tr>
<tr><td><a href="ADR018-reject-rabitq-vector-codec.md">018</a></td><td>2026-07-30</td><td>Accepted</td><td></td><td><a href="ADR018-reject-rabitq-vector-codec.md">Reject RaBitQ as a production vector codec</a></td><td></td></tr>
<tr><td><a href="ADR019-reject-product-quantisation.md">019</a></td><td>2026-07-30</td><td>Accepted</td><td></td><td><a href="ADR019-reject-product-quantisation.md">Reject product quantisation at the default search budget</a></td><td></td></tr>
<tr><td><a href="ADR020-stop-hybrid-retrieval-2-research.md">020</a></td><td>2026-07-30</td><td>Accepted</td><td></td><td><a href="ADR020-stop-hybrid-retrieval-2-research.md">Stop the Hybrid Retrieval 2.0 research branch</a></td><td></td></tr>
<tr><td><a href="ADR021-rowles-text-analysis-package.md">021</a></td><td>2026-07-31</td><td>Accepted</td><td></td><td><a href="ADR021-rowles-text-analysis-package.md">Package Analysis independently while preserving LeanCorpus source inclusion</a></td><td></td></tr>
<tr><td><a href="ADR022-parent-linked-incremental-backups.md">022</a></td><td>2026-08-05</td><td>Accepted</td><td></td><td><a href="ADR022-parent-linked-incremental-backups.md">Parent-linked manifests define incremental backup chains</a></td><td></td></tr>
<tr><td><a href="ADR023-immutable-reader-composition-and-ordinals.md">023</a></td><td>2026-08-05</td><td>Accepted</td><td></td><td><a href="ADR023-immutable-reader-composition-and-ordinals.md">Reader composition uses immutable snapshots and term-order ordinals</a></td><td></td></tr>
<tr><td><a href="ADR024-memory-mapped-compound-segment-files.md">024</a></td><td>2026-08-05</td><td>Accepted</td><td></td><td><a href="ADR024-memory-mapped-compound-segment-files.md">Compound segment files use memory-mapped slices</a></td><td></td></tr>
<tr><td><a href="ADR025-unified-codec-catalogue.md">025</a></td><td>2026-08-09</td><td>Accepted</td><td></td><td><a href="ADR025-unified-codec-catalogue.md">Unified codec catalogue defines every persistent format</a></td><td></td></tr>
<tr><td><a href="ADR026-canonical-binary-file-frame.md">026</a></td><td>2026-08-09</td><td>Accepted</td><td><a href="ADR009-codeckit-trailer-streaming.md">009</a></td><td><a href="ADR026-canonical-binary-file-frame.md">Canonical binary files use the self-identifying LCCF Frame v1</a></td><td></td></tr>
<tr><td><a href="ADR027-memory-mapped-operation-lifetimes.md">027</a></td><td>2026-08-21</td><td>Accepted</td><td></td><td><a href="ADR027-memory-mapped-operation-lifetimes.md">Memory mappings drain active operations before reclamation</a></td><td></td></tr>
<tr><td><a href="ADR028-token-graph-analysis.md">028</a></td><td>2026-08-21</td><td>Accepted</td><td></td><td><a href="ADR028-token-graph-analysis.md">Token graphs remain an analysis concern and flatten before postings</a></td><td></td></tr>
<tr><td><a href="ADR029-platform-filesystem-durability.md">029</a></td><td>2026-08-21</td><td>Accepted</td><td></td><td><a href="ADR029-platform-filesystem-durability.md">Platform-specific durability stays behind the Store boundary</a></td><td></td></tr>
</tbody>
</table>
</div>

## Template

New ADRs should follow [the template](_template.md) using the next available
`ADRnnn` prefix.

## Reasons for an ADR

Create an ADR when the decision is costly to reverse, trade-off heavy,
cross-cutting or non-obvious. Major changes to index structure, storage
formats, analysis pipelines, concurrency, merging, scoring or query parsing
also need one.
