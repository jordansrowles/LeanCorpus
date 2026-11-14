using System.Globalization;
using System.Text;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;

namespace Rowles.Text.Tests;

/// <summary>
/// Golden graph output for <see cref="SynonymGraphFilter"/>. The expected strings were recorded
/// from the buffering implementation before the streaming fast path, and the randomised cases
/// compare against an independent copy of that algorithm. Output must stay byte-identical,
/// including order, increments, position lengths, offsets, types and payloads.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Filters)]
public sealed class SynonymGraphFilterGoldenTests
{
    private static readonly (string Source, string[] Replacements)[] StandardMap =
    [
        ("quick", ["fast", "rapid"]),
        ("new york", ["nyc"]),
        ("new york city", ["nyc", "big apple"]),
        ("a b", ["ab"]),
        ("b c", ["bc"]),
        ("été", ["summer"]),
        ("big red dog", ["clifford"]),
    ];

    private static readonly (string Name, string Input)[] Cases =
    [
        ("empty", ""),
        ("no match", "the/0/3 lazy/4/8 dog/9/12"),
        ("single word", "quick/0/5 fox/6/9"),
        ("multi word longest", "new/0/3 york/4/8 city/9/13 park/14/18"),
        ("multi word shorter", "new/0/3 york/4/8 state/9/14"),
        ("prefix at end", "go/0/2 new/3/6"),
        ("prefix then miss", "new/0/3 jersey/4/10"),
        ("overlap greedy", "a/0/1 b/2/3 c/4/5"),
        ("overlap second", "x/0/1 b/2/3 c/4/5"),
        ("stop word gap", "new/0/3 york/8/12@2 city/13/17"),
        ("upper case", "New/0/3 YORK/4/8 Quick/9/14"),
        ("stacked after match", "quick/0/5 QUICK/0/5@0 fox/6/9"),
        ("stacked inside source", "new/0/3 newer/0/5@0 york/6/10"),
        ("first token increment zero", "quick/0/5@0 fox/6/9"),
        ("first token increment three", "quick/0/5@3 fox/6/9"),
        ("negative increment", "fox/0/3 quick/4/9@-2 dog/10/13"),
        ("input position length", "big/0/3 red/4/7#2 dog/8/11 bark/12/16"),
        ("types and payloads", "quick/0/5:word:01ff fox/6/9:word:"),
        ("unicode", "ÉTÉ/0/3 chaud/4/9"),
        ("repeated matches", "quick/0/5 quick/6/11 new/12/15 york/16/20 quick/21/26"),
        ("adjacent sources", "a/0/1 b/2/3 b/4/5 c/6/7"),
        ("long token", "LONG/0/300 quick/301/306"),
    ];

    public static TheoryData<string, string> GoldenCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (name, input) in Cases)
            data.Add(name, input);
        return data;
    }

    [Theory(DisplayName = "Synonym Graph: Golden Output Is Unchanged")]
    [MemberData(nameof(GoldenCases))]
    public void GoldenOutput_IsUnchanged(string name, string input)
    {
        string actual = Render(Run(new SynonymGraphFilter(BuildMap(StandardMap)), ParseStream(input)));

        Assert.Equal(Golden[name], actual);
        Assert.Equal(Render(Reference.Run(StandardMap, ParseStream(input))), actual);
    }

    [Fact(DisplayName = "Synonym Graph: One Filter Resets Between Streams")]
    public void OneFilter_ResetsBetweenStreams()
    {
        var filter = new SynonymGraphFilter(BuildMap(StandardMap));
        foreach (var (_, input) in Cases)
        {
            var stream = ParseStream(input);
            Assert.Equal(Render(Reference.Run(StandardMap, stream)), Render(Run(filter, stream)));
        }
    }

    [Fact(DisplayName = "Synonym Graph: Interleaved Clones Stay Independent")]
    public void InterleavedClones_StayIndependent()
    {
        var map = BuildMap(StandardMap);
        var configured = new SynonymGraphFilter(map);
        var left = configured.Clone();
        var right = configured.Clone();
        var leftStream = ParseStream("new/0/3 york/4/8 city/9/13 quick/14/19");
        var rightStream = ParseStream("a/0/1 b/2/3 c/4/5 new/6/9");
        var leftSink = new MaterialisingTokenSink();
        var rightSink = new MaterialisingTokenSink();

        for (int i = 0; i < Math.Max(leftStream.Count, rightStream.Count); i++)
        {
            if (i < leftStream.Count) Apply(left, leftStream[i], leftSink);
            if (i < rightStream.Count) Apply(right, rightStream[i], rightSink);
        }
        left.Finish(leftSink);
        right.Finish(rightSink);

        Assert.Equal(Render(Reference.Run(StandardMap, leftStream)), Render(leftSink.Tokens));
        Assert.Equal(Render(Reference.Run(StandardMap, rightStream)), Render(rightSink.Tokens));
    }

    [Fact(DisplayName = "Synonym Graph: Randomised Streams Match The Buffering Algorithm")]
    public void RandomisedStreams_MatchBufferingAlgorithm()
    {
        var random = new Random(20_261_113); // NOSONAR: S2245, a seeded generator makes every case reproducible; nothing here is security-sensitive.
        string[] vocabulary = ["a", "b", "c", "d", "A", "B", "e", "C"];
        for (int iteration = 0; iteration < 2_000; iteration++)
        {
            int entries = random.Next(0, 7);
            var map = new List<(string, string[])>();
            for (int e = 0; e < entries; e++)
            {
                int words = random.Next(1, 4);
                string source = string.Join(' ', Enumerable.Range(0, words).Select(_ => vocabulary[random.Next(4)]));
                string[] replacements = Enumerable.Range(0, random.Next(1, 3)).Select(r => $"s{e}{r}").ToArray();
                map.Add((source, replacements));
            }

            int length = random.Next(0, 14);
            var stream = new List<StreamToken>();
            int offset = 0;
            for (int t = 0; t < length; t++)
            {
                string text = vocabulary[random.Next(vocabulary.Length)];
                int increment = random.Next(10) switch { 0 => 0, 1 => 2, 2 => -1, _ => 1 };
                int positionLength = random.Next(6) == 0 ? 2 : 1;
                byte[]? payload = random.Next(5) == 0 ? [(byte)t] : null;
                string type = random.Next(4) == 0 ? "word" : Token.DefaultType;
                stream.Add(new StreamToken(text, offset, offset + text.Length, type, increment, positionLength, payload,
                    UsePositionLength: positionLength != 1 || random.Next(2) == 0));
                offset += text.Length + 1;
            }

            var mapArray = map.ToArray();
            string expected = Render(Reference.Run(mapArray, stream));
            string actual = Render(Run(new SynonymGraphFilter(BuildMap(mapArray)), stream));
            Assert.True(expected == actual,
                $"Iteration {iteration}: map [{string.Join("; ", map.Select(static m => m.Item1 + " => " + string.Join(",", m.Item2)))}], stream [{Render(stream)}]\nexpected {expected}\nactual   {actual}");
        }
    }

    // Recorded from the buffering implementation at c16f3e4e2 before the streaming change.
    private static readonly Dictionary<string, string> Golden = new(StringComparer.Ordinal)
    {
        ["adjacent sources"] = "a/0/1/term/1/1/- ab/0/3/term/0/2/- b/2/3/term/1/1/- b/4/5/term/1/1/- bc/4/7/term/0/2/- c/6/7/term/1/1/-",
        ["empty"] = "",
        ["first token increment three"] = "quick/0/5/term/3/1/- fast/0/5/term/0/1/- rapid/0/5/term/0/1/- fox/6/9/term/1/1/-",
        ["first token increment zero"] = "quick/0/5/term/1/1/- fast/0/5/term/0/1/- rapid/0/5/term/0/1/- fox/6/9/term/1/1/-",
        ["input position length"] = "big/0/3/term/1/1/- clifford/0/11/term/0/3/- red/4/7/term/1/2/- dog/8/11/term/1/1/- bark/12/16/term/1/1/-",
        ["long token"] = "QQQ~300/0/300/term/1/1/- quick/301/306/term/1/1/- fast/301/306/term/0/1/- rapid/301/306/term/0/1/-",
        ["multi word longest"] = "new/0/3/term/1/1/- nyc/0/13/term/0/3/- big apple/0/13/term/0/3/- york/4/8/term/1/1/- city/9/13/term/1/1/- park/14/18/term/1/1/-",
        ["multi word shorter"] = "new/0/3/term/1/1/- nyc/0/8/term/0/2/- york/4/8/term/1/1/- state/9/14/term/1/1/-",
        ["negative increment"] = "fox/0/3/term/1/1/- quick/4/9/term/0/1/- fast/4/9/term/0/1/- rapid/4/9/term/0/1/- dog/10/13/term/1/1/-",
        ["no match"] = "the/0/3/term/1/1/- lazy/4/8/term/1/1/- dog/9/12/term/1/1/-",
        ["overlap greedy"] = "a/0/1/term/1/1/- ab/0/3/term/0/2/- b/2/3/term/1/1/- c/4/5/term/1/1/-",
        ["overlap second"] = "x/0/1/term/1/1/- b/2/3/term/1/1/- bc/2/5/term/0/2/- c/4/5/term/1/1/-",
        ["prefix at end"] = "go/0/2/term/1/1/- new/3/6/term/1/1/-",
        ["prefix then miss"] = "new/0/3/term/1/1/- jersey/4/10/term/1/1/-",
        ["repeated matches"] = "quick/0/5/term/1/1/- fast/0/5/term/0/1/- rapid/0/5/term/0/1/- quick/6/11/term/1/1/- fast/6/11/term/0/1/- rapid/6/11/term/0/1/- new/12/15/term/1/1/- nyc/12/20/term/0/2/- york/16/20/term/1/1/- quick/21/26/term/1/1/- fast/21/26/term/0/1/- rapid/21/26/term/0/1/-",
        ["single word"] = "quick/0/5/term/1/1/- fast/0/5/term/0/1/- rapid/0/5/term/0/1/- fox/6/9/term/1/1/-",
        ["stacked after match"] = "quick/0/5/term/1/1/- QUICK/0/5/term/0/1/- fast/0/5/term/0/1/- rapid/0/5/term/0/1/- fast/0/5/term/0/1/- rapid/0/5/term/0/1/- fox/6/9/term/1/1/-",
        ["stacked inside source"] = "new/0/3/term/1/1/- newer/0/5/term/0/1/- york/6/10/term/1/1/-",
        ["stop word gap"] = "new/0/3/term/1/1/- nyc/0/17/term/0/4/- big apple/0/17/term/0/4/- york/8/12/term/2/1/- city/13/17/term/1/1/-",
        ["types and payloads"] = "quick/0/5/word/1/1/01FF fast/0/5/word/0/1/- rapid/0/5/word/0/1/- fox/6/9/word/1/1/-",
        ["unicode"] = "ÉTÉ/0/3/term/1/1/- summer/0/3/term/0/1/- chaud/4/9/term/1/1/-",
        ["upper case"] = "New/0/3/term/1/1/- nyc/0/8/term/0/2/- YORK/4/8/term/1/1/- Quick/9/14/term/1/1/- fast/9/14/term/0/1/- rapid/9/14/term/0/1/-",
    };

    internal sealed record StreamToken(
        string Text, int StartOffset, int EndOffset, string Type, int Increment, int PositionLength,
        byte[]? Payload, bool UsePositionLength);

    private static List<StreamToken> ParseStream(string input)
    {
        var tokens = new List<StreamToken>();
        foreach (string item in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // text/start/end[@increment][#positionLength][:type:payloadhex]
            string body = item;
            string type = Token.DefaultType;
            byte[]? payload = null;
            int colon = body.IndexOf(':');
            if (colon >= 0)
            {
                string[] parts = body[(colon + 1)..].Split(':');
                type = parts[0];
                payload = parts[1].Length == 0 ? null : Convert.FromHexString(parts[1]);
                body = body[..colon];
            }
            int positionLength = 1;
            bool usePositionLength = false;
            int hash = body.IndexOf('#');
            if (hash >= 0)
            {
                positionLength = int.Parse(body[(hash + 1)..], CultureInfo.InvariantCulture);
                usePositionLength = true;
                body = body[..hash];
            }
            int increment = 1;
            int at = body.IndexOf('@');
            if (at >= 0)
            {
                increment = int.Parse(body[(at + 1)..], CultureInfo.InvariantCulture);
                body = body[..at];
            }
            string[] fields = body.Split('/');
            string text = fields[0] == "LONG" ? new string('Q', 299) + "Ü" : fields[0];
            tokens.Add(new StreamToken(text, int.Parse(fields[1], CultureInfo.InvariantCulture),
                int.Parse(fields[2], CultureInfo.InvariantCulture), type, increment, positionLength, payload, usePositionLength));
        }
        return tokens;
    }

    private static SynonymMap BuildMap((string Source, string[] Replacements)[] entries)
    {
        var map = new SynonymMap();
        foreach (var (source, replacements) in entries)
            map.Add(source, replacements);
        return map;
    }

    private static List<Token> Run(ISpanTokenFilter filter, List<StreamToken> stream)
    {
        var sink = new MaterialisingTokenSink();
        foreach (var token in stream)
            Apply(filter, token, sink);
        filter.Finish(sink);
        return sink.Tokens.ToList();
    }

    private static void Apply(ISpanTokenFilter filter, StreamToken token, ISpanTokenSink sink)
    {
        if (token.UsePositionLength)
            filter.Apply(token.Text.AsSpan(), token.StartOffset, token.EndOffset, token.Type, token.Increment,
                token.PositionLength, token.Payload, sink);
        else
            filter.Apply(token.Text.AsSpan(), token.StartOffset, token.EndOffset, token.Type, token.Increment,
                token.Payload, sink);
    }

    private static string Render(IEnumerable<Token> tokens)
        => string.Join(" ", tokens.Select(static token => string.Create(CultureInfo.InvariantCulture,
            $"{(token.Text.Length > 20 ? token.Text[..3] + "~" + token.Text.Length : token.Text)}/{token.StartOffset}/{token.EndOffset}/{token.Type}/{token.PositionIncrement}/{token.PositionLength}/{(token.Payload is null ? "-" : Convert.ToHexString(token.Payload))}")));

    private static string Render(IEnumerable<StreamToken> tokens)
        => string.Join(" ", tokens.Select(static token => string.Create(CultureInfo.InvariantCulture,
            $"{token.Text}/{token.StartOffset}/{token.EndOffset}@{token.Increment}#{token.PositionLength}{(token.UsePositionLength ? "" : "(6)")}")));

    /// <summary>
    /// Independent copy of the buffering algorithm: materialise every token, convert increments
    /// to absolute starts, greedily take the longest case-insensitive source match from each
    /// unconsumed token, then emit originals and replacements ordered by start and insertion.
    /// </summary>
    private static class Reference
    {
        private sealed class Node
        {
            public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
            public string[]? Replacements { get; set; }
        }

        public static List<Token> Run((string Source, string[] Replacements)[] entries, List<StreamToken> stream)
        {
            var root = new Node();
            foreach (var (source, replacements) in entries)
            {
                var node = root;
                foreach (string word in source.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string key = word.ToLowerInvariant();
                    if (!node.Children.TryGetValue(key, out var child))
                        node.Children[key] = child = new Node();
                    node = child;
                }
                node.Replacements = replacements;
            }

            if (stream.Count == 0)
                return [];

            var edges = new List<(Token Token, int Start, int Order)>();
            var inputs = new List<(Token Token, int Start)>();
            foreach (var item in stream)
            {
                var token = new Token(item.Text, item.StartOffset, item.EndOffset, item.Type, item.Increment, item.Payload,
                    item.UsePositionLength ? item.PositionLength : 1);
                int start = inputs.Count == 0
                    ? Math.Max(0, item.Increment - 1)
                    : inputs[^1].Start + Math.Max(0, item.Increment);
                inputs.Add((token, start));
                edges.Add((token, start, edges.Count));
            }

            int i = 0;
            while (i < inputs.Count)
            {
                var node = root;
                int best = 0;
                string[]? bestReplacements = null;
                for (int j = i; j < inputs.Count; j++)
                {
                    if (!node.Children.TryGetValue(inputs[j].Token.Text.ToLowerInvariant(), out var child))
                        break;
                    node = child;
                    if (node.Replacements is not null)
                    {
                        best = j - i + 1;
                        bestReplacements = node.Replacements;
                    }
                }

                if (best == 0)
                {
                    i++;
                    continue;
                }

                var first = inputs[i];
                var last = inputs[i + best - 1];
                int positionLength = last.Start + last.Token.PositionLength - first.Start;
                foreach (string replacement in bestReplacements!)
                    edges.Add((new Token(replacement, first.Token.StartOffset, last.Token.EndOffset, first.Token.Type, 1, null, positionLength),
                        first.Start, edges.Count));
                i += best;
            }

            var output = new List<Token>();
            int previous = -1;
            foreach (var edge in edges.OrderBy(static edge => edge.Start).ThenBy(static edge => edge.Order))
            {
                output.Add(new Token(edge.Token.Text, edge.Token.StartOffset, edge.Token.EndOffset, edge.Token.Type,
                    edge.Start - previous, edge.Token.Payload, edge.Token.PositionLength));
                previous = edge.Start;
            }
            return output;
        }
    }
}
