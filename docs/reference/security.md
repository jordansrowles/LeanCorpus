# Security and data handling

LeanCorpus is an embedded library. Your application owns authentication, authorisation, encryption and network exposure.

- Keep index directories outside untrusted write paths.
- Apply tenant and permission filters in every search path.
- Treat query text as untrusted input and apply resource limits to broad or expensive queries.
- Do not put secrets, raw tenant identifiers or document content into telemetry unless the application has an explicit policy.
- Encrypt storage through the host filesystem or application boundary. LeanCorpus does not provide transparent index encryption.

See [Per-query resource controls](../searching/09-resource-controls.md) and [Observability](../observability/index.md).
