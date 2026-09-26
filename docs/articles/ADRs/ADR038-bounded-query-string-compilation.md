---
adr: ADR038
title: Validate bounded query syntax before executable query construction
date: 2026-09-26
status: Accepted
version-added: vNext
summary: Apply shared schema and complexity validation to query strings before compiling executable queries.
areas: [query-parsing, search, server]
---

# ADR038: Validate bounded query syntax before executable query construction

- **Date:** 2026-09-26
- **Status:** Accepted

## Context

Structured Server queries pass through schema validation and a request-wide
complexity budget before the translator constructs executable Core `Query`
objects. Query strings previously called the public parser directly, so their
fields and query shape could bypass the same checks. Parsing can also perform
work before a query tree exists, including token creation and phrase token-graph
expansion.

The public Server query DTOs must remain transport-neutral, and ordinary Core
search must retain its specialised query execution paths.

## Decision

Parse query text into an internal, non-executable `QuerySyntax` tree. Bound
parser-time work such as nesting, syntax nodes, tokens and phrase graph
expansion. The Server translator then validates every syntax node against the
compiled schema and a shared request-wide query compilation budget. Structured
query definitions use that same budget. Only after all validation succeeds may
the translator compile syntax into the existing executable Core `Query`
objects.

Keep the syntax model and the parser operations used by Server internal to
`Rowles.LeanCorpus`. Preserve the public `QueryParser.Parse(string)` entry point
by implementing it as parse followed by immediate compilation, without Server
policy. Do not add query syntax types to transport contracts or persisted
formats.

## Rationale

Validating a non-executable tree prevents untrusted text from constructing a
large executable query before Server checks its schema, clause count and
nested depth. Parser-time limits also stop expensive token and phrase-graph
work from growing without bound before that tree is available. Compiling only
to existing built-in query types keeps execution on the tuned search pipeline
described by ADR013.

## Consequences

- Structured and text queries consume one Server request budget for nesting and
  clauses.
- Explicit fields in query text receive the same schema and queryability checks
  as structured fields.
- Server Core uses an existing friend-assembly boundary for internal Core
  syntax access; public API, package references, package versions and stored
  index formats do not change.
- Public Core parser callers retain the same API and receive executable queries
  after parsing completes.
- Changes to syntax nodes or budget accounting must preserve validation before
  compilation and keep parser-time limits bounded.
