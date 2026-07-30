using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Coverage tests for vector-related methods on <see cref="SegmentReader"/>:
/// GetVector(int), GetVector(string, int), EnsureVectorReaderNoLock (cache and missing-path branches).
/// </summary>
[Trait("Category", "Index")]
public sealed class SegmentReaderVectorTests: IDisposable
{
    private readonly string _dir;

    public SegmentReaderVectorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_sr_vec_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    private (MMapDirectory Dir, IndexSearcher Searcher) BuildAndOpen(Action<IndexWriter> populate)
    {
        var mmap = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            populate(writer);
            writer.Commit();
        }
        return (mmap, new IndexSearcher(mmap));
    }

    // GetVector(int docId) — field-less legacy overload

    [Fact(DisplayName = "SegmentReader: GetVector DocId With Vector Field Returns Vector")]
    public void GetVector_DocId_WithVectorField_ReturnsVector()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test", stored: false));
            doc.Add(new VectorField("embed", new float[] { 1f, 2f, 3f }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var vec = reader.GetVector(0);
            Assert.NotNull(vec);
            Assert.Equal(3, vec.Length);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetVector DocId No Vector Field Returns Null")]
    public void GetVector_DocId_NoVectorField_ReturnsNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world"));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Null(reader.GetVector(0));
        }
    }

    // GetVector(string fieldName, int docId)

    [Fact(DisplayName = "SegmentReader: GetVector FieldName DocId Returns Vector")]
    public void GetVector_FieldName_DocId_ReturnsVector()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test", stored: false));
            doc.Add(new VectorField("embed", new float[] { 1f, 2f, 3f }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var vec = reader.GetVector("embed", 0);
            Assert.NotNull(vec);
            Assert.Equal(3, vec.Length);
        }
    }

    [Fact(DisplayName = "SegmentReader: GetVector FieldName Missing Field Returns Null")]
    public void GetVector_FieldName_MissingField_ReturnsNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test", stored: false));
            doc.Add(new VectorField("embed", new float[] { 1f, 2f, 3f }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            Assert.Null(reader.GetVector("nosuchfield", 0));
        }
    }

    [Fact(DisplayName = "SegmentReader: GetVector Empty FieldName Single Vector Field Falls Back To First")]
    public void GetVector_EmptyFieldName_SingleVectorField_FallsBackToFirst()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test", stored: false));
            doc.Add(new VectorField("embed", new float[] { 4f, 5f, 6f }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var vec = reader.GetVector(string.Empty, 0);
            Assert.NotNull(vec);
            Assert.Equal(3, vec.Length);
        }
    }

    // EnsureVectorReaderNoLock — cache and missing-path branches

    [Fact(DisplayName = "SegmentReader: EnsureVectorReaderNoLock Cached Reader Returns Same Instance")]
    public void EnsureVectorReaderNoLock_CachedReader_ReturnsSameInstance()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test", stored: false));
            doc.Add(new VectorField("embed", new float[] { 1f, 2f, 3f }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            var first = reader.GetVector("embed", 0);
            var second = reader.GetVector("embed", 0);
            // Both calls should succeed and return equal vectors (reader is cached).
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first, second);
        }
    }

    [Fact(DisplayName = "SegmentReader: EnsureVectorReaderNoLock Missing Path Returns Null")]
    public void EnsureVectorReaderNoLock_MissingPath_ReturnsNull()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", "test", stored: false));
            doc.Add(new VectorField("embed", new float[] { 1f, 2f, 3f }));
            w.AddDocument(doc);
        });
        using (dir) using (searcher)
        {
            var reader = searcher.GetSegmentReaders()[0];
            // "other" was never registered in _vectorPaths, so EnsureVectorReaderNoLock returns null.
            Assert.Null(reader.GetVector("other", 0));
        }
    }

    [Theory(DisplayName = "SegmentReader: Sparse vector fields preserve missing documents")]
    [InlineData(VectorQuantisation.None)]
    [InlineData(VectorQuantisation.Int8)]
    [InlineData(VectorQuantisation.BBQ)]
    [InlineData(VectorQuantisation.Int4)]
    public void SparseVectorField_PreservesMissingDocuments(VectorQuantisation quantisation)
    {
        using var mmap = new MMapDirectory(Path.Combine(_dir, quantisation.ToString()));
        using (var writer = new IndexWriter(
            mmap,
            new IndexWriterConfig
            {
                BuildHnswOnFlush = false,
                VectorQuantisation = quantisation,
            }))
        {
            var missing = new LeanDocument();
            missing.Add(new TextField("kind", "missing"));
            writer.AddDocument(missing);

            var present = new LeanDocument();
            present.Add(new TextField("kind", "present"));
            present.Add(new VectorField("embed", new float[] { -1f, 0f }));
            writer.AddDocument(present);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var reader = searcher.GetSegmentReaders()[0];
        Assert.Null(reader.GetVector("embed", 0));
        Assert.NotNull(reader.GetVector("embed", 1));

        var results = searcher.Search(new VectorQuery("embed", [1f, 0f]), 10);
        Assert.Single(results.ScoreDocs);
        Assert.Equal(1, results.ScoreDocs[0].DocId);
        Assert.True(results.ScoreDocs[0].Score < 0f);
    }

    [Fact(DisplayName = "Rejected RaBitQ codec cannot create new indexes")]
    public void RejectedRaBitQCodec_CannotCreateNewIndex()
    {
        using var mmap = new MMapDirectory(Path.Combine(_dir, VectorQuantisation.RaBitQ.ToString()));
        var error = Assert.Throws<NotSupportedException>(() => new IndexWriter(
            mmap,
            new IndexWriterConfig
            {
                BuildHnswOnFlush = false,
                VectorQuantisation = VectorQuantisation.RaBitQ,
            }));
        Assert.Contains("rejected by ADR016", error.Message);

        using var perFieldMmap = new MMapDirectory(Path.Combine(_dir, "product_quantisation_field"));
        var perFieldError = Assert.Throws<NotSupportedException>(() => new IndexWriter(
            perFieldMmap,
            new IndexWriterConfig
            {
                VectorFields =
                {
                    ["embed"] = new VectorFieldConfig
                    {
                        Quantisation = VectorQuantisation.ProductQuantisation,
                    },
                },
            }));
        Assert.Contains("rejected by ADR016", perFieldError.Message);
    }

    [Fact(DisplayName = "Rejected product quantisation codec cannot create new indexes")]
    public void RejectedProductQuantisationCodec_CannotCreateNewIndex()
    {
        using var mmap = new MMapDirectory(Path.Combine(_dir, "product_quantisation"));
        var error = Assert.Throws<NotSupportedException>(() => new IndexWriter(
            mmap,
            new IndexWriterConfig
            {
                BuildHnswOnFlush = false,
                VectorQuantisation = VectorQuantisation.ProductQuantisation,
            }));
        Assert.Contains("rejected by ADR016", error.Message);
    }

    [Fact(DisplayName = "Byte vectors: round-trip and query through the ordinary vector lifecycle")]
    public void ByteVectors_RoundTripAndQuery()
    {
        using var mmap = new MMapDirectory(Path.Combine(_dir, "byte_vectors"));
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig
        {
            BuildHnswOnFlush = false,
            NormaliseVectors = false,
        }))
        {
            var first = new LeanDocument();
            first.Add(new ByteVectorField("embed", new byte[] { 1, 2, 3, 4 }));
            writer.AddDocument(first);
            var second = new LeanDocument();
            second.Add(new ByteVectorField("embed", new byte[] { 4, 3, 2, 1 }));
            writer.AddDocument(second);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var result = searcher.Search(new ByteVectorQuery("embed", new byte[] { 1, 2, 3, 4 }, topK: 1), 1);

        Assert.Single(result.ScoreDocs);
        Assert.Equal(0, result.ScoreDocs[0].DocId);
        Assert.Equal(new float[] { 1f, 2f, 3f, 4f }, searcher.GetSegmentReaders()[0].GetVector("embed", 0));
    }

    [Fact(DisplayName = "VectorQuery: Field dimension mismatch fails before scoring")]
    public void VectorQuery_FieldDimensionMismatch_FailsBeforeScoring()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var doc = new LeanDocument();
            doc.Add(new VectorField("embed", new float[] { 1f, 0f, 0f }));
            w.AddDocument(doc);
        });
        using (dir)
        using (searcher)
        {
            var exception = Assert.Throws<ArgumentException>(
                () => searcher.Search(new VectorQuery("embed", [1f, 0f]), 10));
            Assert.Contains("dimension 2", exception.Message);
            Assert.Contains("dimension 3", exception.Message);
        }
    }

    [Theory(DisplayName = "VectorQuery: Per-field similarity controls scoring")]
    [InlineData(VectorSimilarityFunction.Cosine, 0)]
    [InlineData(VectorSimilarityFunction.DotProduct, 1)]
    [InlineData(VectorSimilarityFunction.Euclidean, 0)]
    [InlineData(VectorSimilarityFunction.MaximumInnerProduct, 1)]
    public void VectorQuery_PerFieldSimilarity_ControlsScoring(
        VectorSimilarityFunction similarity,
        int expectedFirstDoc)
    {
        string path = Path.Combine(_dir, "similarity_" + similarity);
        using var mmap = new MMapDirectory(path);
        using (var writer = new IndexWriter(
            mmap,
            new IndexWriterConfig
            {
                VectorFields =
                {
                    ["embed"] = new VectorFieldConfig
                    {
                        Similarity = similarity,
                        Normalise = false,
                        BuildHnsw = false,
                    },
                },
            }))
        {
            var first = new LeanDocument();
            first.Add(new VectorField("embed", new float[] { 1f, 0f }));
            writer.AddDocument(first);

            var second = new LeanDocument();
            second.Add(new VectorField("embed", new float[] { 2f, 0f }));
            writer.AddDocument(second);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var results = searcher.Search(new VectorQuery("embed", [1f, 0f]), 2);
        Assert.Equal(expectedFirstDoc, results.ScoreDocs[0].DocId);
    }

    [Fact(DisplayName = "VectorQuery: Quantised field can retain exact Float32 reranking")]
    public void VectorQuery_QuantisedField_CanRetainExactFloat32Reranking()
    {
        string path = Path.Combine(_dir, "retained_float");
        using var mmap = new MMapDirectory(path);
        float[] original = [-0.2f, 0.98f];
        using (var writer = new IndexWriter(
            mmap,
            new IndexWriterConfig
            {
                VectorFields =
                {
                    ["embed"] = new VectorFieldConfig
                    {
                        Quantisation = VectorQuantisation.BBQ,
                        RetainFullPrecision = true,
                        Normalise = false,
                        BuildHnsw = false,
                    },
                },
            }))
        {
            var doc = new LeanDocument();
            doc.Add(new VectorField("embed", original));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var reader = searcher.GetSegmentReaders()[0];
        var restored = reader.GetVector("embed", 0);
        Assert.Equal(original, restored);

        var results = searcher.Search(new VectorQuery("embed", original), 1);
        Assert.Equal(1f, results.ScoreDocs[0].Score, 5);
    }

    [Theory(DisplayName = "Vector merge: Sparse presence survives force merge")]
    [InlineData(VectorQuantisation.None)]
    [InlineData(VectorQuantisation.Int8)]
    [InlineData(VectorQuantisation.BBQ)]
    [InlineData(VectorQuantisation.Int4)]
    public void SparseVectorPresence_SurvivesForceMerge(VectorQuantisation quantisation)
    {
        string path = Path.Combine(_dir, "merge_" + quantisation);
        using var mmap = new MMapDirectory(path);
        using (var writer = new IndexWriter(
            mmap,
            new IndexWriterConfig
            {
                MaxBufferedDocs = 2,
                MergePolicy = NoMergePolicy.Instance,
                VectorFields =
                {
                    ["embed"] = new VectorFieldConfig
                    {
                        Quantisation = quantisation,
                        RetainFullPrecision = quantisation != VectorQuantisation.None,
                        Normalise = false,
                        BuildHnsw = false,
                    },
                },
            }))
        {
            writer.AddDocument(new LeanDocument());

            var firstVector = new LeanDocument();
            firstVector.Add(new VectorField("embed", new float[] { -1f, 0f }));
            writer.AddDocument(firstVector);
            writer.Commit();

            var secondVector = new LeanDocument();
            secondVector.Add(new VectorField("embed", new float[] { 1f, 0f }));
            writer.AddDocument(secondVector);
            writer.AddDocument(new LeanDocument());
            writer.Commit();

            Assert.Equal(2, writer.ForceMerge(1));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var reader = Assert.Single(searcher.GetSegmentReaders());
        Assert.Null(reader.GetVector("embed", 0));
        Assert.Equal(
            [-1f, 0f],
            Assert.IsType<float[]>(reader.GetVector("embed", 1)));
        Assert.Equal(
            [1f, 0f],
            Assert.IsType<float[]>(reader.GetVector("embed", 2)));
        Assert.Null(reader.GetVector("embed", 3));
    }

    [Fact(DisplayName = "Vector merge: Per-field configuration survives force merge")]
    public void PerFieldVectorConfiguration_SurvivesForceMerge()
    {
        string path = Path.Combine(_dir, "merge_config");
        using var mmap = new MMapDirectory(path);
        using (var writer = new IndexWriter(
            mmap,
            new IndexWriterConfig
            {
                MaxBufferedDocs = 2,
                MergePolicy = NoMergePolicy.Instance,
                VectorFields =
                {
                    ["embed"] = new VectorFieldConfig
                    {
                        Similarity = VectorSimilarityFunction.DotProduct,
                        Quantisation = VectorQuantisation.Int8,
                        RetainFullPrecision = true,
                        Normalise = false,
                        BuildHnsw = true,
                        HnswBuildConfig = new Rowles.LeanCorpus.Codecs.Hnsw.HnswBuildConfig
                        {
                            M = 8,
                            M0 = 12,
                            EfConstruction = 40,
                        },
                    },
                },
            }))
        {
            for (int i = 1; i <= 4; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new VectorField("embed", new float[] { i, 1f, 0f }));
                writer.AddDocument(doc);
            }
            writer.Commit();
            Assert.Equal(2, writer.ForceMerge(1));
            writer.Commit();
        }

        string segmentPath = Directory.GetFiles(path, "seg_*.seg")
            .OrderBy(file => int.Parse(
                Path.GetFileNameWithoutExtension(file).AsSpan("seg_".Length)))
            .Last();
        var field = Assert.Single(
            Rowles.LeanCorpus.Index.Segment.SegmentInfo.ReadFrom(segmentPath).VectorFields);
        Assert.Equal(VectorSimilarityFunction.DotProduct, field.Similarity);
        Assert.Equal(VectorQuantisation.Int8, field.Quantisation);
        Assert.True(field.RetainsFullPrecision);
        Assert.True(field.HasHnsw);
        Assert.Equal(8, field.HnswM);
        Assert.Equal(12, field.HnswM0);
        Assert.Equal(40, field.HnswEfConstruction);
    }

    [Theory(DisplayName = "Vector HNSW: Int4 codec flushes and searches")]
    [InlineData(VectorQuantisation.Int4)]
    public void Int4Codec_HnswFlushesAndSearches(VectorQuantisation quantisation)
    {
        string path = Path.Combine(_dir, "hnsw_" + quantisation);
        using var mmap = new MMapDirectory(path);
        using (var writer = new IndexWriter(
            mmap,
            new IndexWriterConfig
            {
                VectorFields =
                {
                    ["embed"] = new VectorFieldConfig
                    {
                        Quantisation = quantisation,
                        Normalise = false,
                        BuildHnsw = true,
                    },
                },
            }))
        {
            for (int docId = 0; docId < 24; docId++)
            {
                var document = new LeanDocument();
                document.Add(new VectorField(
                    "embed",
                    new float[]
                    {
                        MathF.Sin(docId),
                        MathF.Cos(docId),
                        docId / 24f,
                        (docId % 3) - 1f,
                        (docId % 5) / 5f,
                    }));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var segment = Assert.Single(searcher.GetSegmentReaders());
        Assert.Equal(quantisation, Assert.Single(segment.Info.VectorFields).Quantisation);
        Assert.True(Assert.Single(segment.Info.VectorFields).HasHnsw);

        var results = searcher.Search(
            new VectorQuery("embed", [MathF.Sin(7), MathF.Cos(7), 7f / 24f, 0f, 2f / 5f]),
            10);
        Assert.NotEmpty(results.ScoreDocs);
        Assert.Contains(results.ScoreDocs, hit => hit.DocId == 7);

        var validation = IndexValidator.Check(mmap, new IndexCheckOptions { Deep = true });
        Assert.DoesNotContain(
            validation.DetailedIssues,
            issue => issue.Severity == IndexCheckSeverity.Error);
    }

    [Fact(DisplayName = "VectorSimilarityQuery: Returns only scores meeting threshold")]
    public void VectorSimilarityQuery_ReturnsOnlyScoresMeetingThreshold()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var exact = new LeanDocument();
            exact.Add(new VectorField("embed", new float[] { 1f, 0f }));
            w.AddDocument(exact);

            var below = new LeanDocument();
            below.Add(new VectorField("embed", new float[] { 0.5f, 0.5f }));
            w.AddDocument(below);
        });
        using (dir)
        using (searcher)
        {
            var results = searcher.Search(
                new VectorSimilarityQuery(
                    "embed",
                    [1f, 0f],
                    minimumSimilarity: 0.8f,
                    maxResults: 10),
                10);

            Assert.Equal(1, results.TotalHits);
            Assert.Equal(0, Assert.Single(results.ScoreDocs).DocId);
        }
    }

    [Fact(DisplayName = "Vector merge: Deletion remaps sparse vector ordinals")]
    public void Deletion_RemapsSparseVectorOrdinalsDuringMerge()
    {
        string path = Path.Combine(_dir, "delete_merge");
        using var mmap = new MMapDirectory(path);
        using (var writer = new IndexWriter(
            mmap,
            new IndexWriterConfig
            {
                MaxBufferedDocs = 2,
                MergePolicy = NoMergePolicy.Instance,
                VectorFields =
                {
                    ["embed"] = new VectorFieldConfig
                    {
                        Quantisation = VectorQuantisation.Int8,
                        RetainFullPrecision = true,
                        Normalise = false,
                        BuildHnsw = false,
                    },
                },
            }))
        {
            var missing = new LeanDocument();
            missing.Add(new StringField("id", "missing"));
            writer.AddDocument(missing);

            var removed = new LeanDocument();
            removed.Add(new StringField("id", "remove"));
            removed.Add(new VectorField("embed", new float[] { -1f, 0f }));
            writer.AddDocument(removed);
            writer.Commit();

            var kept = new LeanDocument();
            kept.Add(new StringField("id", "keep"));
            kept.Add(new VectorField("embed", new float[] { 1f, 0f }));
            writer.AddDocument(kept);
            writer.Commit();

            writer.DeleteDocuments(new TermQuery("id", "remove"));
            writer.Commit();
            Assert.Equal(2, writer.ForceMerge(1));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var reader = Assert.Single(searcher.GetSegmentReaders());
        Assert.Equal(2, reader.MaxDoc);
        var vectors = Enumerable.Range(0, reader.MaxDoc)
            .Select(docId => reader.GetVector("embed", docId))
            .ToArray();
        Assert.Single(vectors, vector => vector is null);
        Assert.Equal(
            [1f, 0f],
            Assert.IsType<float[]>(Assert.Single(vectors, vector => vector is not null)));
        var result = searcher.Search(new VectorQuery("embed", [1f, 0f]), 10);
        Assert.Equal(1, result.TotalHits);
        Assert.Equal(1f, Assert.Single(result.ScoreDocs).Score, 5);
    }

    [Theory(DisplayName = "Vector diagnostics: Reports score provenance")]
    [InlineData(VectorQuantisation.None, false, VectorScoreProvenance.ExactFloat32)]
    [InlineData(VectorQuantisation.Int8, false, VectorScoreProvenance.ReconstructedQuantised)]
    [InlineData(VectorQuantisation.Int8, true, VectorScoreProvenance.ExactFloat32)]
    public void VectorDiagnostics_ReportsScoreProvenance(
        VectorQuantisation quantisation,
        bool retainFullPrecision,
        VectorScoreProvenance expected)
    {
        string path = Path.Combine(
            _dir,
            $"diagnostics_{quantisation}_{retainFullPrecision}");
        using var mmap = new MMapDirectory(path);
        using (var writer = new IndexWriter(
            mmap,
            new IndexWriterConfig
            {
                VectorFields =
                {
                    ["embed"] = new VectorFieldConfig
                    {
                        Quantisation = quantisation,
                        RetainFullPrecision = retainFullPrecision,
                        BuildHnsw = false,
                    },
                },
            }))
        {
            var doc = new LeanDocument();
            doc.Add(new VectorField("embed", new float[] { 1f, 0f }));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var execution = searcher.SearchWithDiagnostics(
            new VectorQuery("embed", [1f, 0f], topK: 10),
            10);

        Assert.Equal(expected, execution.Diagnostics.ScoreProvenance);
        Assert.Equal(SearchExecutionStrategy.VectorFlatScan, execution.Diagnostics.Strategy);
        Assert.True(execution.Diagnostics.ExactCandidateSet);
        Assert.Equal(10, execution.Diagnostics.CandidateLimit);
        Assert.Equal(1, execution.Diagnostics.ReturnedCount);
        Assert.Equal(SearchCompletionState.Completed, execution.Diagnostics.Completion);
    }

    [Fact(DisplayName = "Vector diagnostics: Reports HNSW candidate generation")]
    public void VectorDiagnostics_ReportsHnswCandidateGeneration()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new VectorField("embed", new float[] { i + 1f, 0f }));
                w.AddDocument(doc);
            }
        });
        using (dir)
        using (searcher)
        {
            var execution = searcher.SearchWithDiagnostics(
                new VectorQuery("embed", [1f, 0f]),
                10);

            Assert.Equal(SearchExecutionStrategy.VectorHnsw, execution.Diagnostics.Strategy);
            Assert.False(execution.Diagnostics.ExactCandidateSet);
            Assert.Equal(VectorScoreProvenance.ExactFloat32, execution.Diagnostics.ScoreProvenance);
            Assert.True(execution.Diagnostics.HnswNodesVisited > 0);
            Assert.Equal(0, execution.Diagnostics.HnswRetryCount);
        }
    }

    [Fact(DisplayName = "Vector diagnostics: Reports exhausted HNSW visit budget")]
    public void VectorDiagnostics_ReportsExhaustedHnswVisitBudget()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            for (int i = 0; i < 12; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new VectorField("embed", new float[] { i + 1f, 0f }));
                w.AddDocument(doc);
            }
        });
        using (dir)
        using (searcher)
        {
            var execution = searcher.SearchWithDiagnostics(
                new VectorQuery("embed", [1f, 0f], maxVisitedNodes: 1),
                10);

            Assert.Equal(SearchCompletionState.BudgetExhausted, execution.Diagnostics.Completion);
            Assert.True(execution.Diagnostics.HnswBudgetExhausted);
            Assert.InRange(execution.Diagnostics.HnswNodesVisited, 0, 1);
        }
    }

    [Fact(DisplayName = "Vector diagnostics: Partial flat search is never reported as exact")]
    public void VectorDiagnostics_PartialFlatSearchIsNeverReportedAsExact()
    {
        using var mmap = new MMapDirectory(Path.Combine(_dir, "partial_flat_diagnostics"));
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig { BuildHnswOnFlush = false }))
        {
            for (int i = 0; i < 4; i++)
            {
                var document = new LeanDocument();
                document.Add(new VectorField("embed", new float[] { i + 1f, 0f }));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var execution = searcher.SearchWithDiagnostics(
            new VectorQuery("embed", new float[] { 1f, 0f }),
            topN: 2,
            SearchOptions.WithTimeout(TimeSpan.Zero));

        Assert.True(execution.Results.IsPartial);
        Assert.Equal(SearchCompletionState.BudgetExhausted, execution.Diagnostics.Completion);
        Assert.False(execution.Diagnostics.ExactCandidateSet);
    }

    [Fact(DisplayName = "Seeded vector query: Uses global document seed within visit budget")]
    public void SeededVectorQuery_UsesGlobalDocumentSeedWithinVisitBudget()
    {
        var (dir, searcher) = BuildAndOpen(w =>
        {
            var seed = new LeanDocument();
            seed.Add(new VectorField("embed", new float[] { 1f, 0f }));
            w.AddDocument(seed);
            for (int i = 0; i < 11; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new VectorField("embed", new float[] { 0f, i + 1f }));
                w.AddDocument(doc);
            }
        });
        using (dir)
        using (searcher)
        {
            var results = searcher.Search(
                new SeededVectorQuery(
                    "embed",
                    [1f, 0f],
                    seedDocumentIds: [0],
                    topK: 1,
                    maxVisitedNodes: 1),
                1);

            Assert.Equal(0, Assert.Single(results.ScoreDocs).DocId);
        }
    }

}
