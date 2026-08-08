---
title: ADR003: Community security defaults and private identity
description: Keeps secrets, cryptographic material and commercial identity ownership outside public server contracts.
version: vNext
status: Accepted
date: 2026-08-07
---

# ADR003: Community security defaults and private identity

## Context

Local Community Server must be simple to host, while Enterprise identity, cluster PKI and licence validation need independent security and recovery decisions.

## Decision

Authentication is represented by a framework-neutral provider. The Community default returns an anonymous caller so the host can supply its own policy. Public contracts contain no private keys, certificates, customer identity, node secrets or cryptographic implementation types. Licence validation accepts an opaque envelope only.

Cluster identity, PKI lifecycle, signing algorithm selection, secure secret storage and entitlement verification are private-module decisions. Enterprise endpoints require authorisation and entitlement ports.

## Consequences

The Community API remains hostable without a bundled identity system. Private security changes do not alter the public DTO boundary, and insecure secret handling cannot become an accidental public contract.
