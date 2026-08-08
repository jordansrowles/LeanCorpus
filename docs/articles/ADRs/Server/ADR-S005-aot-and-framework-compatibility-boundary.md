---
title: ADR005: AOT and framework compatibility boundary
description: Keeps the public contracts Native AOT-ready while isolating framework-specific compatibility work.
version: vNext
status: Accepted
date: 2026-08-07
---

# ADR005: AOT and framework compatibility boundary

## Context

The server needs Native AOT support, but ASP.NET Core, gRPC, Studio and optional clustering infrastructure have different trimming and runtime constraints.

## Decision

The abstractions package targets `net10.0` and `net11.0`, uses only the BCL, and supplies `System.Text.Json` source-generated metadata. It does not reference ASP.NET Core, gRPC, Blazor, DotNext or private infrastructure.

Framework compatibility is verified by the owning host or private module before it is claimed. The abstraction boundary itself is the shared AOT gate, not proof that every future host is AOT-compatible.

## Consequences

Public contracts remain safe to consume from different hosts. Each host retains responsibility for its own publish, trimming and smoke-test evidence.
