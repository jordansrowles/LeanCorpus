# Studio

Rowles.LeanCorpus.Studio is an embeddable Razor Class Library. Register it with AddLeanCorpusStudio(), enable static files, and map it with MapLeanCorpusStudio(). The reference host serves it at:

~~~text
http://127.0.0.1:5080/studio
~~~

Studio calls the public Community REST endpoints. It does not parse index files or use a private storage path.

The alpha workflow includes:

- server health and readiness;
- index listing, creation, selection and confirmed deletion;
- schema and statistics;
- bounded document and segment inspection;
- document indexing;
- query and explanation test benches;
- mutable settings.

Indexed and source values are inserted as text, and destructive deletion remains protected by the server-side confirmation token. There are no cluster, replica, shard or licence pages. Read-your-writes is represented by the public write token and consistency controls rather than a Studio-only operation.
