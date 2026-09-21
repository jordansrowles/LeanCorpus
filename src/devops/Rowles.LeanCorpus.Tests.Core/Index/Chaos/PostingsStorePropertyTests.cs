using System.Text;
using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer.Postings;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Chaos)]
[Area(TestArea.Index)]
public sealed class PostingsStorePropertyTests
{
    private static readonly string[] Fields = ["body", "title", "タイトル"];
    private static readonly string[] Vocabulary = ["alpha", "λ", "значение", "alpha"];

    [Property(DisplayName = "Generated postings scenarios match the reference model", MaxTest = 200, StartSize = 1, EndSize = 64)]
    public void GeneratedPostings_MatchReferenceModel(NonEmptyArray<byte> input)
    {
        byte[] bytes = input.Get;
        var model = new Dictionary<string, ModelTerm>(StringComparer.Ordinal);
        using var store = new PostingsStore(storePayloads: true, storeTermVectors: true);

        int documentCount = Math.Max(1, (bytes.Length + 7) / 8);
        for (int document = 0; document < documentCount; document++)
        {
            int baseIndex = document * 8;
            byte first = ByteAt(bytes, baseIndex);
            string field = Fields[first % Fields.Length];
            string term = Vocabulary[ByteAt(bytes, baseIndex + 1) % Vocabulary.Length];
            int operationCount = 1 + ByteAt(bytes, baseIndex + 3) % 3;

            for (int operation = 0; operation < operationCount; operation++)
            {
                FieldIndexOptions options = (FieldIndexOptions)(ByteAt(bytes, baseIndex + 2 + operation * 2) % 4);
                int position = 1 + operation + ByteAt(bytes, baseIndex + 1) % 3;
                byte marker = ByteAt(bytes, baseIndex + 3 + operation * 2);
                byte[]? payload = options >= FieldIndexOptions.DocsAndFreqsAndPositions && (marker & 1) != 0
                    ? Enumerable.Range(0, 1 + marker % 4).Select(value => (byte)(value ^ marker)).ToArray()
                    : null;
                int startOffset = operation * 4 + marker % 3;
                int endOffset = startOffset + 1 + (payload?.Length ?? 0);

                if (options == FieldIndexOptions.DocsOnly)
                    store.AddDocOnly(field, term, document);
                else
                    store.Add(field, term, document, position, options, payload, startOffset, endOffset);

                ApplyModel(model, field, term, document, position, options, payload, startOffset, endOffset);
            }
        }

        store.Freeze();

        var expectedTerms = model.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray();
        var actualTerms = Enumerable.Range(0, store.TermCount)
            .Select(termId => Encoding.UTF8.GetString(store.GetTerm(termId)))
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedTerms, actualTerms);

        foreach (var (qualifiedTerm, expected) in model)
        {
            int termId = store.TermHash.Find(Encoding.UTF8.GetBytes(qualifiedTerm));
            Assert.True(termId >= 0, qualifiedTerm);
            ref readonly var state = ref store.GetTermState(termId);
            Assert.Equal(expected.Flags, state.Flags);

            var expectedPostings = expected.Postings.Values.ToArray();
            var docReader = store.OpenDocReader(termId);
            var actualPostings = new List<PostingDoc>(expectedPostings.Length);
            while (docReader.MoveNext(out var posting))
                actualPostings.Add(posting);

            Assert.Equal(expectedPostings.Length, actualPostings.Count);
            var proxReader = store.OpenProxReader(termId);
            for (int postingIndex = 0; postingIndex < expectedPostings.Length; postingIndex++)
            {
                ModelPosting expectedPosting = expectedPostings[postingIndex];
                PostingDoc actualPosting = actualPostings[postingIndex];
                Assert.Equal(expectedPosting.DocId, actualPosting.DocId);
                Assert.Equal(expectedPosting.Freq, actualPosting.Freq);
                Assert.Equal(expectedPosting.Positions.Count, actualPosting.PositionCount);

                if (!state.Flags.HasFlag(PostingFlags.HasPositions))
                    continue;

                proxReader.StartDocument();
                for (int positionIndex = 0; positionIndex < expectedPosting.Positions.Count; positionIndex++)
                {
                    Assert.True(proxReader.ReadNext(out var actualPosition));
                    Assert.Equal(expectedPosting.Positions[positionIndex], actualPosition.Position);
                    Assert.Equal(expectedPosting.Payloads[positionIndex], ReadPayload(ref proxReader, actualPosition.PayloadLength));
                    Assert.Equal(expectedPosting.HasOffsets[positionIndex], actualPosition.HasOffsets);
                    if (expectedPosting.HasOffsets[positionIndex])
                    {
                        Assert.Equal(expectedPosting.StartOffsets[positionIndex], actualPosition.StartOffset);
                        Assert.Equal(expectedPosting.EndOffsets[positionIndex], actualPosition.EndOffset);
                    }
                }
            }
            Assert.False(proxReader.ReadNext(out _));
        }
    }

    [Property(DisplayName = "Generated arena streams round-trip through varied chunks", MaxTest = 200, StartSize = 1, EndSize = 128)]
    public void GeneratedArena_RoundTripsThroughVariedChunks(NonEmptyArray<byte> input)
    {
        byte[] expected = input.Get;
        using var arena = new PostingsByteArena();
        var cursor = arena.StartStream();

        int written = 0;
        while (written < expected.Length)
        {
            int chunk = 1 + expected[written] % 23;
            chunk = Math.Min(chunk, expected.Length - written);
            arena.WriteBytes(ref cursor, expected.AsSpan(written, chunk));
            written += chunk;
        }

        byte[] actual = new byte[expected.Length];
        var reader = arena.OpenReader(cursor);
        int read = 0;
        while (read < actual.Length)
        {
            int chunk = 1 + expected[read] % 19;
            chunk = Math.Min(chunk, actual.Length - read);
            reader.CopyBytes(actual.AsSpan(read, chunk));
            read += chunk;
        }

        Assert.Equal(expected, actual);
        Assert.True(reader.EndOfStream);
    }

    private static byte ByteAt(byte[] bytes, int index) => bytes[index % bytes.Length];

    private static byte[] ReadPayload(ref PostingProxReader proxReader, int length)
    {
        byte[] payload = new byte[length];
        proxReader.CopyPayloadTo(payload);
        return payload;
    }

    private static void ApplyModel(
        Dictionary<string, ModelTerm> model,
        string field,
        string term,
        int docId,
        int position,
        FieldIndexOptions options,
        byte[]? payload,
        int startOffset,
        int endOffset)
    {
        string qualifiedTerm = $"{field}\0{term}";
        if (!model.TryGetValue(qualifiedTerm, out var modelTerm))
            model[qualifiedTerm] = modelTerm = new ModelTerm();

        modelTerm.Flags |= FlagsFor(options, payload);
        if (!modelTerm.Postings.TryGetValue(docId, out var posting))
            modelTerm.Postings[docId] = posting = new ModelPosting(docId);

        if (options >= FieldIndexOptions.DocsAndFreqs)
            posting.Freq = posting.Freq == 0 ? 1 : checked(posting.Freq + 1);
        if (options < FieldIndexOptions.DocsAndFreqsAndPositions)
            return;

        posting.Positions.Add(position);
        posting.Payloads.Add(payload ?? []);
        bool hasOffsets = options == FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets;
        posting.HasOffsets.Add(hasOffsets);
        posting.StartOffsets.Add(hasOffsets ? startOffset : 0);
        posting.EndOffsets.Add(hasOffsets ? endOffset : 0);
    }

    private static PostingFlags FlagsFor(FieldIndexOptions options, byte[]? payload)
    {
        PostingFlags flags = PostingFlags.None;
        if (options >= FieldIndexOptions.DocsAndFreqs)
            flags |= PostingFlags.HasFreqs;
        if (options >= FieldIndexOptions.DocsAndFreqsAndPositions)
            flags |= PostingFlags.HasPositions;
        if (options == FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets)
            flags |= PostingFlags.HasOffsets;
        if (payload is { Length: > 0 })
            flags |= PostingFlags.HasPayloads;
        return flags;
    }

    private sealed class ModelTerm
    {
        internal PostingFlags Flags { get; set; }
        internal SortedDictionary<int, ModelPosting> Postings { get; } = [];
    }

    private sealed class ModelPosting(int docId)
    {
        internal int DocId { get; } = docId;
        internal int Freq { get; set; }
        internal List<int> Positions { get; } = [];
        internal List<byte[]> Payloads { get; } = [];
        internal List<bool> HasOffsets { get; } = [];
        internal List<int> StartOffsets { get; } = [];
        internal List<int> EndOffsets { get; } = [];
    }
}
