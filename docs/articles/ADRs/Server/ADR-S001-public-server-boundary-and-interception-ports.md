---
title: ADR001: Public server boundary and interception ports
description: Keeps Community Server usable while reserving distributed and commercial implementation details for private modules.
version: vNext
status: Accepted
date: 2026-08-07
---

# ADR001: Public server boundary and interception ports

## Context

Community Server must remain usable as a public local server package. Distributed routing, entitlement enforcement, auditing and clustered durability must be replaceable without exposing their implementation or making them dependencies of the Community path.

## Decision

`Rowles.LeanCorpus.Server.Abstractions` is a BCL-only, transport-neutral public package. It contains contracts, service interfaces, an authentication hook and the eight ports: routing, authorisation, entitlement, write acknowledgement, index lifecycle, audit, consistency and inspection.

The package also supplies Community defaults. They route locally, use local fsync acknowledgement, support local consistency and otherwise provide no-op or host-replaceable behaviour. Private modules may replace these ports but must not require the public package to reference them.

## Consequences

The Core and ASP.NET packages can remain public Community packages. Clustering, commercial entitlement, remote backup and operational implementation remain private. Every mapped endpoint records the ports that its implementation must use.
