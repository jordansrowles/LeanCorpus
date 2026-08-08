---
title: ADR002: Versioned transport-neutral contracts
description: Defines stable server DTOs separately from engine, ASP.NET and gRPC implementation types.
version: vNext
status: Accepted
date: 2026-08-07
---

# ADR002: Versioned transport-neutral contracts

## Context

The server must offer REST, gRPC and Studio-facing operations without leaking LeanCorpus engine query types or framework types into its public boundary.

## Decision

Public DTOs model index administration, documents, all planned query shapes, search pagination, facets, distributed result metadata, bounded inspection and Enterprise administration. REST remains versioned under `/v1`; a source-controlled OpenAPI 3.1 document and protobuf source declare transport bindings. The endpoint catalogue classifies each route by edition, access and required ports.

`System.Text.Json` source generation is the only JSON metadata mechanism. The public query hierarchy is transport-owned and does not expose core query classes.

## Consequences

Hosts adapt the same contracts to REST and gRPC. Contract evolution is additive within a version, and incompatible changes require a new API version. Core and host implementation remain free to evolve behind the interfaces.
