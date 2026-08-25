# Search and explain

The Community query contract supports query string, term, Boolean, phrase, prefix, wildcard, regexp, span-near and vector queries. Query translation validates fields, types, nesting, clause counts, regular-expression size and vector dimensions before execution.

Search supports configured result limits, deterministic document-ID tie-breaking, search-after cursors, score and compatible field sorts, terms facets and document projection. Highlights are deliberately unsupported in 0.1 and return a typed failure. Timings report the measured server-side search duration.

Explain uses the same translator as Search. The engine currently provides term and vector explanations; other query types return `explain_not_supported`.
