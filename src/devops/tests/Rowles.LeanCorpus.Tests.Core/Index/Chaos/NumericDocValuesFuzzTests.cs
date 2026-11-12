using FsCheck.Xunit;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Aggregations;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Core.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Core.Index;
[Category(TestCategory.Chaos)]
[Area(TestArea.Index)]
[Technique(TestTechnique.PropertyBased)]
public sealed class NumericDocValuesFuzzTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;
    public NumericDocValuesFuzzTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 200)]
    public void WriteRead_DoubleValues_RoundTrip(double[] values)
    {
        if (values.Length == 0 || values.Length > 1000) return;

        var fieldMap = new Dictionary<string, double[]>(StringComparer.Ordinal)
        {
            ["f"] = values
        };

        string basePath = System.IO.Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N"));
        NumericDocValuesWriter.Write(basePath, fieldMap, values.Length);

        Assert.True(File.Exists(basePath), "Numeric doc values file not written");

        var (readValues, _) = NumericDocValuesReader.Read(basePath);
        Assert.True(readValues.TryGetValue("f", out var actual), "Field 'f' not found in read-back");

        Assert.Equal(values.Length, actual.Length);
        for (int i = 0; i < values.Length; i++)
            Assert.Equal(values[i], actual[i], 0.0001);
    }

    /// <summary>
    /// The single-value read must return exactly what the materialising path returns: a document
    /// with one value reads it as a single value, a document with several keeps the multi-value
    /// list, and a document without values reads nothing. Inputs include NaN, infinities, signed
    /// zeros, missing values and multi-valued documents.
    /// </summary>
    [Property(MaxTest = 60)]
    public void SortedNumeric_SingleValueReads_MatchMaterialisedValues(double[][] documents)
    {
        if (documents is null || documents.Length == 0 || documents.Length > 200) return;
        documents = documents.Select(static values => (values ?? []).Take(3).ToArray()).ToArray();

        string path = System.IO.Path.Combine(_fixture.Path, "sorted_single_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = documents.Length + 1 }))
        {
            for (int i = 0; i < documents.Length; i++)
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                foreach (double value in documents[i])
                {
                    document.Add(new NumericField("d", value));
                    document.Add(new Int64Field("l", BitConverter.DoubleToInt64Bits(value)));
                }
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var reader = Assert.Single(searcher.GetSegmentReaders());
        var readers = searcher.GetSegmentReaders();
        bool anyValues = documents.Any(static values => values.Length > 0);
        var doubleAccessor = anyValues ? NumericFieldValues.ResolveFieldAccessor("d", readers) : default;
        var int64Accessor = anyValues ? NumericFieldValues.ResolveFieldAccessor("l", readers) : default;

        for (int docId = 0; docId < documents.Length; docId++)
        {
            bool expectedDouble = reader.TryGetSortedNumericDocValues("d", docId, out var doubleList);
            bool actualDouble = NumericFieldValues.TryRead(reader, "d", docId, doubleAccessor, out var doubleValues);
            Assert.Equal(expectedDouble && doubleList.Count > 0, actualDouble);
            if (actualDouble)
                Assert.Equal(Bits(doubleList), Bits(doubleValues.DoubleValues ?? [doubleValues.DoubleValue]));
            Assert.Equal(doubleList.Count > 1, doubleValues.DoubleValues is not null);

            bool expectedInt64 = reader.TryGetSortedInt64DocValues("l", docId, out var int64List);
            bool actualInt64 = NumericFieldValues.TryRead(reader, "l", docId, int64Accessor, out var int64Values);
            Assert.Equal(expectedInt64 && int64List.Count > 0, actualInt64);
            if (actualInt64)
                Assert.Equal(int64List, int64Values.Int64Values ?? [int64Values.Int64Value]);
            Assert.Equal(int64List.Count > 1, int64Values.Int64Values is not null);
        }
    }

    private static long[] Bits(IEnumerable<double> values) => values.Select(BitConverter.DoubleToInt64Bits).ToArray();
}
