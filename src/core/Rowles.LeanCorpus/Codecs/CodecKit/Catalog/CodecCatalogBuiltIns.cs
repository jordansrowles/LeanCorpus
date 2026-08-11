namespace Rowles.LeanCorpus.Codecs.CodecKit;

internal static class CodecCatalogBuiltIns
{
    internal static readonly IReadOnlyList<CodecFamilyDescriptor> Families =
    [
        Family("leancorpus.term-dictionary", "Term dictionary",
            VersionedFile("leancorpus.term-dictionary.data", "leancorpus.term-dictionary", "Term dictionary", ".dic", CodecConstants.TermDictionaryVersion, CodecAccessKind.Materialised)),
        Family("leancorpus.postings", "Postings",
            VersionedFile("leancorpus.postings.data", "leancorpus.postings", "Postings", ".pos", CodecConstants.PostingsVersion, CodecAccessKind.Streaming,
                legacyFraming: CodecLegacyFraming.CodecKitEnvelope | CodecLegacyFraming.CodecKitTrailer | CodecLegacyFraming.CustomHeader)),
        Family("leancorpus.norms", "Norms",
            VersionedFile("leancorpus.norms.data", "leancorpus.norms", "Norms", ".nrm", CodecConstants.NormsVersion, CodecAccessKind.Materialised)),
        Family("leancorpus.field-lengths", "Field lengths",
            VersionedFile("leancorpus.field-lengths.data", "leancorpus.field-lengths", "Field lengths", ".fln", CodecConstants.FieldLengthVersion, CodecAccessKind.Materialised)),
        Family("leancorpus.stored-fields", "Stored fields",
            VersionedFile("leancorpus.stored-fields.data", "leancorpus.stored-fields", "Stored fields data", ".fdt", CodecConstants.StoredFieldsVersion, CodecAccessKind.Streaming,
                CodecMigrationBehaviour.CoordinatedRewrite, CodecLegacyFraming.CodecKitEnvelope | CodecLegacyFraming.CodecKitTrailer | CodecLegacyFraming.CustomHeader),
            VersionedFile("leancorpus.stored-fields.index", "leancorpus.stored-fields", "Stored fields index", ".fdx", CodecConstants.StoredFieldsVersion, CodecAccessKind.RandomAccess,
                CodecMigrationBehaviour.CoordinatedRewrite, CodecLegacyFraming.CodecKitEnvelope | CodecLegacyFraming.CodecKitTrailer | CodecLegacyFraming.CustomHeader)),
        Family("leancorpus.term-vectors", "Term vectors",
            VersionedFile("leancorpus.term-vectors.data", "leancorpus.term-vectors", "Term vectors data", ".tvd", CodecConstants.TermVectorsVersion, CodecAccessKind.Streaming, CodecMigrationBehaviour.CoordinatedRewrite),
            VersionedFile("leancorpus.term-vectors.index", "leancorpus.term-vectors", "Term vectors index", ".tvx", CodecConstants.TermVectorsVersion, CodecAccessKind.RandomAccess, CodecMigrationBehaviour.CoordinatedRewrite)),
        Family("leancorpus.doc-values", "DocValues",
            VersionedFile("leancorpus.doc-values.numeric", "leancorpus.doc-values", "Numeric DocValues", ".dvn", CodecConstants.NumericDocValuesVersion, CodecAccessKind.Streaming),
            VersionedFile("leancorpus.doc-values.sorted", "leancorpus.doc-values", "Sorted DocValues", ".dvs", CodecConstants.SortedDocValuesVersion, CodecAccessKind.Streaming),
            VersionedFile("leancorpus.doc-values.sorted-set", "leancorpus.doc-values", "Sorted-set DocValues", ".dss", CodecConstants.SortedSetDocValuesVersion, CodecAccessKind.Streaming),
            VersionedFile("leancorpus.doc-values.sorted-numeric", "leancorpus.doc-values", "Sorted-numeric DocValues", ".dsn", CodecConstants.SortedNumericDocValuesVersion, CodecAccessKind.Streaming),
            VersionedFile("leancorpus.doc-values.binary", "leancorpus.doc-values", "Binary DocValues", ".dvb", CodecConstants.BinaryDocValuesVersion, CodecAccessKind.Streaming),
            VersionedFile("leancorpus.doc-values.int64", "leancorpus.doc-values", "Int64 DocValues", ".dvnl", CodecConstants.Int64DocValuesVersion, CodecAccessKind.Streaming),
            VersionedFile("leancorpus.doc-values.int64-sorted-numeric", "leancorpus.doc-values", "Int64 sorted-numeric DocValues", ".dsnl", CodecConstants.Int64SortedNumericDocValuesVersion, CodecAccessKind.Streaming)),
        Family("leancorpus.numeric-structures", "Numeric structures",
            VersionedFile("leancorpus.numeric-structures.bkd", "leancorpus.numeric-structures", "BKD tree", ".bkd", CodecConstants.BKDVersion, CodecAccessKind.RandomAccess),
            VersionedFile("leancorpus.numeric-structures.int64-bkd", "leancorpus.numeric-structures", "Int64 BKD tree", ".bkdl", CodecConstants.Int64BKDVersion, CodecAccessKind.RandomAccess),
            VersionedFile("leancorpus.numeric-structures.numeric-index", "leancorpus.numeric-structures", "Numeric field index", ".num", 1, CodecAccessKind.RandomAccess,
                legacyFraming: CodecLegacyFraming.Headerless),
            VersionedFile("leancorpus.numeric-structures.int64-numeric-index", "leancorpus.numeric-structures", "Int64 numeric field index", ".numl", 1, CodecAccessKind.RandomAccess,
                legacyFraming: CodecLegacyFraming.Headerless)),
        Family("leancorpus.vectors", "Vectors",
            VersionedFile("leancorpus.vectors.float32", "leancorpus.vectors", "Vectors", ".vec", CodecConstants.VectorVersion, CodecAccessKind.RandomAccess),
            VersionedFile("leancorpus.vectors.quantised", "leancorpus.vectors", "Quantised vectors", ".vq", CodecConstants.QuantisedVectorVersion, CodecAccessKind.RandomAccess),
            VersionedFile("leancorpus.vectors.hnsw", "leancorpus.vectors", "HNSW", ".hnsw", CodecConstants.HnswVersion, CodecAccessKind.RandomAccess)),
        Family("leancorpus.deletes", "Deletes and bitmaps",
            VersionedFile(
                "leancorpus.deletes.live-docs",
                "leancorpus.deletes",
                "Live docs roaring bitmap",
                ".del",
                CodecConstants.RoaringBitmapVersion,
                CodecAccessKind.Materialised,
                CodecMigrationBehaviour.Rewrite,
                CodecLegacyFraming.Headerless),
            VersionedFile("leancorpus.deletes.parent-bitset", "leancorpus.deletes", "Parent bitset", ".pbs", 1, CodecAccessKind.Materialised,
                legacyFraming: CodecLegacyFraming.Headerless)),
        Family("leancorpus.segment-store", "Segment and store infrastructure",
            VersionlessFile("leancorpus.segment-store.metadata", "leancorpus.segment-store", "Segment metadata", CodecFileMatcher.Extension(".seg"), CodecAccessKind.External,
                [CodecFileMatcher.ExtensionWithTrailingSuffix(".seg", ".tmp")]),
            VersionlessFile("leancorpus.segment-store.statistics", "leancorpus.segment-store", "Segment statistics", CodecFileMatcher.Extension(".stats.json"), CodecAccessKind.External,
                [CodecFileMatcher.ExtensionWithTrailingSuffix(".stats.json", ".tmp")]),
            VersionlessFile("leancorpus.segment-store.compound", "leancorpus.segment-store", "Compound segment", CodecFileMatcher.Extension(".cfs"), CodecAccessKind.External,
                [CodecFileMatcher.ExtensionWithTrailingSuffix(".cfs", ".tmp")], CodecFramingPolicy.Container),
            VersionlessFile("leancorpus.segment-store.commit", "leancorpus.segment-store", "Commit metadata", CodecFileMatcher.Numbered("segments_"), CodecAccessKind.External,
                [CodecFileMatcher.Numbered("segments_", ".tmp")]),
            VersionlessFile("leancorpus.segment-store.commit-statistics", "leancorpus.segment-store", "Commit statistics", CodecFileMatcher.Numbered("stats_", ".json"), CodecAccessKind.External,
                [CodecFileMatcher.Numbered("stats_", ".json.tmp")]),
            VersionlessFile("leancorpus.segment-store.migration-state", "leancorpus.segment-store", "Migration recovery state", CodecFileMatcher.Exact("migration_state.json"), CodecAccessKind.External,
                [CodecFileMatcher.Exact("migration_state.json.tmp")])),
    ];

    private static CodecFamilyDescriptor Family(
        string familyId,
        string displayName,
        params CodecFileDescriptor[] files)
        => new(familyId, displayName, files);

    private static CodecFileDescriptor VersionedFile(
        string formatId,
        string familyId,
        string displayName,
        string extension,
        int currentVersion,
        CodecAccessKind accessKind,
        CodecMigrationBehaviour currentMigrationBehaviour = CodecMigrationBehaviour.Reframe,
        CodecLegacyFraming legacyFraming = CodecLegacyFraming.CodecKitEnvelope | CodecLegacyFraming.CodecKitTrailer)
    {
        var versions = new CodecVersionDescriptor[currentVersion];
        for (var version = 1; version <= currentVersion; version++)
        {
            versions[version - 1] = new CodecVersionDescriptor(
                version,
                $"{formatId}-v{version}",
                isReadable: true,
                isWritable: version == currentVersion,
                legacyFraming,
                migrationBehaviour: currentMigrationBehaviour == CodecMigrationBehaviour.CoordinatedRewrite
                    ? CodecMigrationBehaviour.CoordinatedRewrite
                    : version == currentVersion
                        ? currentMigrationBehaviour
                        : CodecMigrationBehaviour.Rewrite);
        }

        return new CodecFileDescriptor(
            formatId,
            familyId,
            displayName,
            CodecFileMatcher.Extension(extension),
            currentVersion,
            versions,
            accessKind,
            CodecFramingPolicy.Canonical,
            CodecChecksumPolicy.XxHash64,
            currentMigrationBehaviour,
            [CodecFileMatcher.ExtensionWithTrailingSuffix(extension, ".tmp")]);
    }

    private static CodecFileDescriptor VersionlessFile(
        string formatId,
        string familyId,
        string displayName,
        CodecFileMatcher matcher,
        CodecAccessKind accessKind,
        IEnumerable<CodecFileMatcher> temporaryFileMatchers,
        CodecFramingPolicy framingPolicy = CodecFramingPolicy.External)
        => new(
            formatId,
            familyId,
            displayName,
            matcher,
            currentFormatVersion: null,
            accessKind: accessKind,
            currentFraming: framingPolicy,
            checksumPolicy: CodecChecksumPolicy.None,
            migrationBehaviour: CodecMigrationBehaviour.None,
            temporaryFileMatchers: temporaryFileMatchers);
}
