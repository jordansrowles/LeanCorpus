# Documentation

The site is built by DocFX from canonical Markdown under `docs/content/`, YAML navigation, XML API comments, staged reports, and the theme source under `docs/site-config/template/`.

## Source and generated content

Edit canonical Markdown under `docs/content/` and site implementation under
`docs/site-config/`. Do not edit generated output beneath `artifacts/`,
including `artifacts/docs/api`, `artifacts/docs/content`, `artifacts/docs/site`,
generated benchmark and coverage pages, or SDK `bin` and `obj` output.

API reference pages are generated from XML documentation. If a public member is unclear, improve its XML comment as well as any conceptual guide.

The feature comparison lives in the Markdown pages under
`docs/content/articles/features`. User-visible feature additions or removals
should update the relevant comparison page where applicable. Keep Lucene.NET
4.8 and current Java Lucene as separate comparisons, and use `◐` only for
comparable rather than API-equivalent behaviour.

Repository READMEs and contribution guides remain at their owning repository
paths so they work on GitHub. The documentation build reads
`docs/site-config/imports.json`, stages selected files under
`artifacts/docs/content/`, rewrites local links for their site destinations,
and adds a source notice that links to the current commit. Update that manifest
when adding or moving an imported guide.

## Navigation

The root navigation is authored at `docs/content/toc.yml`. Generated sections
add their own local indexes and TOCs in `artifacts/docs/content/`. Add a page to
the appropriate local TOC when introducing a new hierarchy.

Use relative links between conceptual pages. DocFX validates links during the documentation build.

## Diagrams

Use Mermaid for relationships, sequence, state, and flow diagrams. DocFX Modern renders fenced `mermaid` blocks with its built-in renderer, including dark-theme support.

Good choices include:

| Need | Mermaid form |
|---|---|
| Request or commit ordering | `sequenceDiagram` |
| Decision or data flow | `flowchart` |
| Package relationships | `architecture-beta` |
| Compact fixed field layout | `packet-beta` |

Packet diagrams become awkward when a format has variable-length fields, nested frames, or annotations that are not byte offsets. Codec framing therefore uses Bytefield-SVG as a build-time authoring tool.

Bytefield sources live in `docs/site-config/diagrams`. The diagram workflow
stages generated SVG files under `artifacts/docs/content/assets/diagrams`, so
ordinary site builds consume prepared assets without rewriting canonical
documentation.

```powershell
cd docs/site-config/diagrams
npm ci
npm run diagrams
```

Pin diagram-generation dependencies and commit the lock file. Review generated
assets together with their source.

Benchmark charts use the locally staged Chart.js bundle. Its exact package
version is pinned in the diagram package manifest and lock file; generated
pages do not load Chart.js from a CDN.

## Build

Build the site:

```powershell
pwsh ./devops.ps1 docs build -SkipBenchmarks
```

The console groups DocFX warnings by code rather than printing every instance. Full JSON-lines diagnostics are retained under `artifacts/docs/diagnostics`, and long DocFX stages report elapsed time every 30 seconds.

Resolve broken links, duplicate headings, invalid YAML, and Mermaid parse failures before handing off a documentation change. Generated HTML belongs to the build output and should not be committed manually.

All prepared inputs and site output live under ignored `artifacts/docs/`. A
second documentation build should produce the same content without changing
Git status.

## Style

Use British English (just for consistency). Prefer a direct explanation, then an example, then operational caveats. State defaults explicitly when they affect behaviour. Distinguish public API from contributor internals and avoid promising a binary layout that the implementation does not guarantee.
