namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Immutable compact radix trie for allocation-free longest-prefix lookup over UTF-16 text.
/// </summary>
internal sealed class LexiconPrefixTrie
{
    private readonly Node[] _nodes;
    private readonly Edge[] _edges;
    private readonly char[] _labels;

    internal LexiconPrefixTrie(IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        var transitions = new Dictionary<ulong, int>();
        var terminalNodes = new List<bool> { false };

        foreach (string word in words)
        {
            int node = 0;
            foreach (char character in word)
            {
                ulong key = ((ulong)(uint)node << 16) | character;
                if (!transitions.TryGetValue(key, out int child))
                {
                    child = terminalNodes.Count;
                    transitions.Add(key, child);
                    terminalNodes.Add(false);
                }

                node = child;
            }

            terminalNodes[node] = true;
        }

        var buildEdges = new BuildEdge[transitions.Count];
        int buildEdgeIndex = 0;
        foreach ((ulong key, int target) in transitions)
        {
            buildEdges[buildEdgeIndex++] = new BuildEdge(
                (int)(key >> 16),
                (char)(key & 0xFFFF),
                target);
        }

        Array.Sort(buildEdges, static (left, right) =>
        {
            int parentOrder = left.Parent.CompareTo(right.Parent);
            return parentOrder != 0 ? parentOrder : left.Character.CompareTo(right.Character);
        });

        var edgeCounts = new int[terminalNodes.Count];
        foreach (BuildEdge edge in buildEdges)
            edgeCounts[edge.Parent]++;

        var expandedNodes = new Node[terminalNodes.Count];
        int firstEdge = 0;
        for (int node = 0; node < expandedNodes.Length; node++)
        {
            expandedNodes[node] = new Node(firstEdge, edgeCounts[node], terminalNodes[node]);
            firstEdge += edgeCounts[node];
        }

        var compactNodes = new List<Node> { default };
        var compactEdges = new List<Edge>(buildEdges.Length);
        var compactLabels = new List<char>(buildEdges.Length);
        var pending = new Queue<(int OriginalNode, int CompactNode)>();
        pending.Enqueue((0, 0));

        while (pending.TryDequeue(out (int OriginalNode, int CompactNode) current))
        {
            Node originalNode = expandedNodes[current.OriginalNode];
            int firstCompactEdge = compactEdges.Count;

            for (int edgeIndex = originalNode.FirstEdge;
                 edgeIndex < originalNode.FirstEdge + originalNode.EdgeCount;
                 edgeIndex++)
            {
                BuildEdge first = buildEdges[edgeIndex];
                int target = first.Target;
                int labelStart = compactLabels.Count;
                compactLabels.Add(first.Character);

                while (!expandedNodes[target].IsTerminal && expandedNodes[target].EdgeCount == 1)
                {
                    BuildEdge next = buildEdges[expandedNodes[target].FirstEdge];
                    compactLabels.Add(next.Character);
                    target = next.Target;
                }

                int compactTarget = compactNodes.Count;
                compactNodes.Add(default);
                compactEdges.Add(new Edge(labelStart, compactLabels.Count - labelStart, compactTarget));
                pending.Enqueue((target, compactTarget));
            }

            compactNodes[current.CompactNode] = new Node(
                firstCompactEdge,
                compactEdges.Count - firstCompactEdge,
                originalNode.IsTerminal);
        }

        _nodes = compactNodes.ToArray();
        _edges = compactEdges.ToArray();
        _labels = compactLabels.ToArray();
    }

    /// <summary>
    /// Returns the longest terminal prefix of <paramref name="input"/> beginning at
    /// <paramref name="start"/>, measured in UTF-16 code units.
    /// </summary>
    internal int FindLongest(ReadOnlySpan<char> input, int start, int end)
    {
        int nodeIndex = 0;
        int longestMatch = 0;

        int index = start;
        while (index < end)
        {
            Node node = _nodes[nodeIndex];
            if (!TryGetChild(node, input[index], out Edge edge))
                break;

            if (edge.LabelLength > end - index
                || !input.Slice(index, edge.LabelLength).SequenceEqual(
                    _labels.AsSpan(edge.LabelStart, edge.LabelLength)))
            {
                break;
            }

            index += edge.LabelLength;
            nodeIndex = edge.Target;
            if (_nodes[nodeIndex].IsTerminal)
                longestMatch = index - start;
        }

        return longestMatch;
    }

    private bool TryGetChild(Node node, char character, out Edge child)
    {
        int low = node.FirstEdge;
        int end = low + node.EdgeCount;
        int high = end;

        while (low < high)
        {
            int middle = low + ((high - low) >> 1);
            Edge edge = _edges[middle];
            if (_labels[edge.LabelStart] < character)
                low = middle + 1;
            else
                high = middle;
        }

        if (low < end && _labels[_edges[low].LabelStart] == character)
        {
            child = _edges[low];
            return true;
        }

        child = default;
        return false;
    }

    private readonly record struct Node(int FirstEdge, int EdgeCount, bool IsTerminal);

    private readonly record struct Edge(int LabelStart, int LabelLength, int Target);

    private readonly record struct BuildEdge(int Parent, char Character, int Target);
}
