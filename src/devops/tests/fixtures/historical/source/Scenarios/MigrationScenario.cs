using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.HistoricalFixtures;

internal static class MigrationScenario
{
    public static void Generate(string outputRoot, string packageArchive)
    {

        const int seed = 311400;
        const string unicode = "caf\u00e9 \u6771\u4eac \U00010400 \U0001f600";
        if (FixtureOutput.Sha256(File.ReadAllBytes(packageArchive)) != "cf124d5a2b9b3bf623cd69f98ea25c5a2e3f7d67ac9bdb4c017a60a00d00e478" ||
            FixtureOutput.Sha256(File.ReadAllBytes(typeof(IndexWriter).Assembly.Location)) != "6a633412002f615c36579490b6f7b5be57e6b237778bb92bd24492b943d6060c")
            throw new InvalidDataException("The generator requires the pinned published LeanCorpus 3.1.1 package and assembly.");
        var vectors = new Dictionary<int, float[]>
        {
            [0] = [0, 0], [1] = [-1, 0], [3] = [1, 0], [4] = [0, 1], [7] = [0.8f, 0.6f],
        };
        var documents = Enumerable.Range(0, 8).Select(id =>
        {
            var stored = new SortedDictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["id"] = [$"doc-{id:00}"], ["body"] = [$"migration shared document number {id}"],
                ["empty"] = [""], ["small"] = [$"small-{id}"], ["unicode"] = [unicode],
            };
            if (id == 3) stored["large"] = [string.Concat(Enumerable.Repeat(unicode + " large stored value ", 4096))];
            return new
            {
                Id = $"doc-{id:00}", GlobalDocId = id, IsLive = id is not (1 or 5),
                Numeric = id is 2 or 6 ? (double?)null : id * 0.5,
                Sorted = id is 2 or 6 ? null : $"category-{id % 3}",
                Binary = id is 2 or 6 ? null : Convert.ToHexString(id == 3 ? [] : new byte[] { (byte)id, 0xff, 0, (byte)(id + 1) }),
                Vector = vectors.GetValueOrDefault(id),
                GeoPoint = id is 0 or 3 or 7 ? new[] { 51.5 + id * 0.01, -0.12 } : null,
                StoredValues = stored,
            };
        }).ToArray();

        foreach (bool compound in new[] { false, true })
        {
            string layout = compound ? "compound" : "loose";
            string path = Path.Combine(outputRoot, layout);
            Directory.CreateDirectory(path);
            using (var directory = new MMapDirectory(path))
            using (var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                MaxBufferedDocs = 100, MergePolicy = NoMergePolicy.Instance,
                UseCompoundFile = false, DurableCommits = true, BuildHnswOnFlush = true,
                HnswSeed = seed, NormaliseVectors = false,
                IndexSort = new IndexSort(SortField.String("sort:id")),
            }))
            {
                var random = new Random(seed);
                for (int segment = 0; segment < 2; segment++)
                {
                    foreach (int id in Enumerable.Range(segment * 4, 4).OrderBy(_ => random.Next()))
                    {
                        var expected = documents[id];
                        var document = new LeanDocument();
                        document.Add(new StringField("id", expected.Id, stored: true));
                        document.Add(new StringField("sort:id", expected.Id));
                        document.Add(new TextField("body", expected.StoredValues["body"][0], stored: true));
                        foreach (var (name, values) in expected.StoredValues.Where(pair => pair.Key is not ("id" or "body")))
                            foreach (string value in values) document.Add(new StoredField(name, value));
                        if (expected.Numeric is { } number) document.Add(new NumericField("score", number));
                        if (expected.Sorted is { } category) document.Add(new StringField("category", category));
                        if (expected.Binary is { } payload) document.Add(new BinaryField("payload", Convert.FromHexString(payload)));
                        if (expected.Vector is { } vector) document.Add(new VectorField("embedding", vector));
                        if (expected.GeoPoint is { } point) document.Add(new GeoPointField("location", point[0], point[1]));
                        writer.AddDocument(document);
                    }
                    writer.Commit();
                }
                writer.DeleteDocuments(new TermQuery("id", "doc-01"));
                writer.DeleteDocuments(new TermQuery("id", "doc-05"));
                writer.Commit();
            }

            if (compound)
            {
                // The released compound writer cannot apply these term deletions.
                // Commit them loose first, then invoke that package's existing packer.
                // No current source or synthetic legacy codec bytes are involved.
                var packer = typeof(IndexWriter).Assembly.GetType("Rowles.LeanCorpus.Store.CompoundFileWriter", throwOnError: true)!;
                var pack = packer.GetMethod("Pack", BindingFlags.Static | BindingFlags.NonPublic)!;
                foreach (string metadata in Directory.GetFiles(path, "*.seg"))
                {
                    var info = SegmentInfo.ReadFrom(metadata);
                    if (pack.Invoke(null, [path, info.SegmentId]) is not true)
                        throw new InvalidDataException("The released compound packer did not pack the segment.");
                    info.IsCompoundFile = true;
                    info.WriteTo(metadata);
                }
            }

            string[] neighbours;
            string[] geoHits;
            object[] segments;
            var storedById = new Dictionary<string, object>(StringComparer.Ordinal);
            using (var directory = new MMapDirectory(path))
            using (var searcher = new IndexSearcher(directory))
            {
                var readers = Directory.GetFiles(path, "*.seg").Order(StringComparer.Ordinal)
                    .Select(SegmentInfo.ReadFrom).Select(info => new SegmentReader(directory, info)).ToArray();
                if (readers.Length != 2 || readers.Sum(reader => reader.MaxDoc) != 8 ||
                    readers.Sum(reader => Enumerable.Range(0, reader.MaxDoc).Count(id => !reader.IsLive(id))) != 2 ||
                    readers.Any(reader => Enumerable.Range(0, reader.MaxDoc).Count(id => !reader.IsLive(id)) != 1))
                    throw new InvalidDataException("The released writer must persist one deletion in each of two segments.");
                neighbours = searcher.Search(new VectorQuery("embedding", [1, 0], topK: 2, efSearch: 32), 2)
                    .ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["id"].Single()).ToArray();
                if (!neighbours.SequenceEqual(new[] { "doc-03", "doc-07" }))
                    throw new InvalidDataException("The deterministic released-package vector query changed.");
                geoHits = searcher.Search(new GeoDistanceQuery("location", 51.53, -0.12, 100), 8)
                    .ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["id"].Single()).ToArray();
                if (!geoHits.SequenceEqual(new[] { "doc-03" }))
                    throw new InvalidDataException("The deterministic released-package Geo query changed.");
                string[] sortedIds = searcher.Search(new TermQuery("body", "migration"), 10, SortField.String("sort:id"))
                    .ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["id"].Single()).ToArray();
                if (!sortedIds.SequenceEqual(documents.Where(doc => doc.IsLive).Select(doc => doc.Id)))
                    throw new InvalidDataException("The released package must sort the colon-containing field correctly.");
                if (readers.Any(reader => reader.Info.IndexSortFields is not { } sort || !sort.SequenceEqual(new[] { "String:sort:id:False" })))
                    throw new InvalidDataException("The released package must persist its unchanged colon sort metadata.");
                int docBase = 0;
                segments = readers.Select(reader =>
                {
                    int offset = docBase;
                    docBase += reader.MaxDoc;
                    if (!reader.Info.VectorFields.Single().HasHnsw)
                        throw new InvalidDataException("The released writer did not persist an HNSW graph.");
                    // Record the supplied IDs independently of the dense v1 payload.
                    // Acceptance checks the actual graph against these expected IDs.
                    int[] present = vectors.Keys.Where(id => id >= offset && id < offset + reader.MaxDoc)
                        .Select(id => id - offset).Order().ToArray();
                    return (object)new
                    {
                        reader.Info.SegmentId, DocBase = offset, DocCount = reader.MaxDoc,
                        DeletedDocIds = Enumerable.Range(0, reader.MaxDoc).Where(id => !reader.IsLive(id)).ToArray(),
                        VectorDocIds = present, IndexSort = reader.Info.IndexSortFields,
                    };
                }).ToArray();
                foreach (var reader in readers)
                {
                    for (int id = 0; id < reader.MaxDoc; id++)
                    {
                        var values = reader.GetStoredFields(id);
                        storedById.Add(values["id"].Single(), values.ToDictionary(pair => pair.Key,
                            pair => pair.Value.Select(value => new
                            {
                                Utf16Length = value.Length, Sha256 = FixtureOutput.Sha256(Encoding.UTF8.GetBytes(value)),
                            }).ToArray()));
                    }
                    reader.Dispose();
                }
            }

            var manifest = new
            {
                FixtureFormatVersion = 1, LeanCorpusPackageVersion = "3.1.1", GenerationSeed = seed,
                GeneratorSdk = "10.0.103",
                GenerationMethod = compound ? "3.1.1 loose writer, committed deletions, 3.1.1 compound packer" : "3.1.1 loose writer",
                PackageSha256 = FixtureOutput.Sha256(File.ReadAllBytes(packageArchive)),
                PackageAssemblySha256 = FixtureOutput.Sha256(File.ReadAllBytes(typeof(IndexWriter).Assembly.Location)),
                GeneratorFramework = "net10.0", Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Layout = layout, IsCompound = compound, LogicalDocumentCount = 8, DeletedDocumentCount = 2,
                VectorField = "embedding", VectorBearingDocumentIds = vectors.Keys.Order().ToArray(),
                VectorQuery = new { Values = new float[] { 1, 0 }, TopK = 2, ExpectedNeighbourIds = neighbours },
                GeoQuery = new { Field = "location", Latitude = 51.53, Longitude = -0.12, RadiusMetres = 100, ExpectedIds = geoHits },
                Segments = segments,
                Documents = documents.Select(document => new
                {
                    document.Id, document.GlobalDocId, document.IsLive, Stored = storedById[document.Id],
                    document.Numeric, document.Sorted, document.Binary, document.Vector, document.GeoPoint,
                }).ToArray(),
                Files = Directory.GetFiles(path).Order(StringComparer.Ordinal).Select(file => new
                {
                    Name = Path.GetFileName(file), Length = new FileInfo(file).Length, Sha256 = FixtureOutput.Sha256(File.ReadAllBytes(file)),
                }).ToArray(),
            };
            File.WriteAllText(Path.Combine(outputRoot, $"3.1.1-release-{layout}.manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine($"Generated {layout}: eight documents, two committed deletions, sparse vectors and HNSW.");
        }
    }
}
