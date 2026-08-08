# Documentation

The site is built by DocFX from Markdown, YAML navigation, XML API comments, generated reports, and the custom template under `docs/templates/leancorpus`.

## Source and generated content

Edit the Markdown source and templates. Do not edit generated output in:

- `docs/site`;
- `docs/api`;
- `docs/bench`;
- `docs/coverage`;
- `bin` or `obj`.

API reference pages are generated from XML documentation. If a public member is unclear, improve its XML comment as well as any conceptual guide.

The feature-comparison records live in `docs/articles/features/items`. The docs
build reads their front matter and replaces `docs/articles/features/index.md`
with the interactive table, so edit the item files rather than the generated
index.

## Navigation

The root `docs/toc.yml` defines the main site navigation. Each section has its own `toc.yml`. Add a page to both the DocFX content patterns and the appropriate table of contents when introducing a new hierarchy.

Use relative links between conceptual pages. DocFX validates links during the documentation build.

## Diagrams

Use Mermaid for relationships, sequence, state, and flow diagrams. LeanCorpus pins Mermaid 11.16.0 independently of DocFX and loads it only on pages containing a `mermaid-latest` code block.

Good choices include:

| Need | Mermaid form |
|---|---|
| Request or commit ordering | `sequenceDiagram` |
| Decision or data flow | `flowchart` |
| Package relationships | `architecture-beta` |
| Compact fixed field layout | `packet-beta` |

Packet diagrams become awkward when a format has variable-length fields, nested frames, or annotations that are not byte offsets. Codec framing therefore uses Bytefield-SVG as a build-time authoring tool.

Bytefield sources live in `docs/diagrams`. Generated SVG files live in `docs/assets/diagrams` and are committed so ordinary DocFX builds remain .NET-only.

```powershell
cd docs/diagrams
npm ci
npm run diagrams
```

The diagram build also copies the pinned Mermaid browser bundle into `docs/assets/diagrams`. Pin diagram dependencies and commit the lock file. Review generated assets together with their source.

## Build

Build the site without regenerating benchmarks:

```powershell
./devops docs -SkipBenchmarks
```

Resolve broken links, duplicate headings, invalid YAML, and Mermaid parse failures before handing off a documentation change. Generated HTML belongs to the build output and should not be committed manually.

## Style

Use British English (just for consistency). Prefer a direct explanation, then an example, then operational caveats. State defaults explicitly when they affect behaviour. Distinguish public API from contributor internals and avoid promising a binary layout that the implementation does not guarantee.
