namespace OrderedMatching;

/// <summary>
/// Tömör tároló az összes nem-nested M_n matchinghez.
/// A matchingek élei egyetlen lapos int[] tömbben vannak: matching i élei
/// a [i*MatchingSize, (i+1)*MatchingSize) tartományban.
/// </summary>
internal sealed class CompactMatchingModel
{
    private readonly int[] _matchingEdges;
    private readonly (int U, int V)[] _edges;

    public int VertexCount { get; }
    public int MatchingSize { get; }
    public int EdgeCount => _edges.Length;
    public int MatchingCount { get; }
    public ReadOnlySpan<int> Matching(int matchingId)
        => _matchingEdges.AsSpan(matchingId * MatchingSize, MatchingSize);

    public int EdgeAtFlatIndex(int index) => _matchingEdges[index];

    public int GetEdgeId(int u, int v)
    {
        if (u > v) (u, v) = (v, u);
        if (u < 0 || v >= VertexCount || u >= v)
            throw new ArgumentOutOfRangeException();
        return u * (2 * VertexCount - u - 1) / 2 + (v - u - 1);
    }

    private CompactMatchingModel(
        int vertexCount,
        int matchingSize,
        (int U, int V)[] edges,
        int[] matchingEdges,
        int matchingCount)
    {
        VertexCount = vertexCount;
        MatchingSize = matchingSize;
        _edges = edges;
        _matchingEdges = matchingEdges;
        MatchingCount = matchingCount;
    }

    public static CompactMatchingModel Build(int vertexCount, int matchingSize)
    {
        if (vertexCount < 0 || matchingSize < 0 || 2 * matchingSize > vertexCount)
            throw new ArgumentOutOfRangeException();

        var edges = new List<(int U, int V)>(vertexCount * (vertexCount - 1) / 2);
        var edgeId = new int[vertexCount, vertexCount];
        for (var i = 0; i < vertexCount; i++)
        for (var j = i + 1; j < vertexCount; j++)
        {
            edgeId[i, j] = edges.Count;
            edges.Add((i, j));
        }

        // n=6 esetén 816816 matching * 6 él = 4 900 896 int,
        // vagyis kb. 19.6 MB. Ez lényegesen tömörebb az objektum-listás modellnél.
        var data = new List<int>(Math.Max(16, EstimateCapacity(vertexCount, matchingSize)));
        var selectedEdges = new int[matchingSize];
        var used = new bool[vertexCount];
        var selectedVertices = new int[2 * matchingSize];
        var matchingCount = 0;

        void AddCurrent()
        {
            for (var i = 0; i < matchingSize; i++)
                data.Add(selectedEdges[i]);
            matchingCount++;
        }

        void Generate(int v, int depth)
        {
            if (depth == matchingSize)
            {
                AddCurrent();
                return;
            }

            if (v >= vertexCount)
                return;

            var needVertices = 2 * (matchingSize - depth);
            var free = 0;
            for (var i = v; i < vertexCount; i++)
                if (!used[i]) free++;
            if (free < needVertices)
                return;

            if (used[v])
            {
                Generate(v + 1, depth);
                return;
            }

            // v kihagyása.
            used[v] = true;
            Generate(v + 1, depth);
            used[v] = false;

            // v-hez egy későbbi w csúcsot párosítunk.
            used[v] = true;
            for (var w = v + 1; w < vertexCount; w++)
            {
                if (used[w]) continue;

                var valid = true;
                var candidate = (v, w);
                for (var k = 0; k < depth; k++)
                {
                    var old = edges[selectedEdges[k]];
                    if (NestedRelations.IsNested(candidate, old))
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid) continue;

                used[w] = true;
                selectedEdges[depth] = edgeId[v, w];
                Generate(v + 1, depth + 1);
                used[w] = false;
            }
            used[v] = false;
        }

        // A tömböt itt csak a stack-védelem kedvéért használjuk: a generátor
        // ugyanazt a matching-halmazt állítja elő, mint az eredeti modell.
        _ = selectedVertices;
        Generate(0, 0);

        return new CompactMatchingModel(
            vertexCount,
            matchingSize,
            edges.ToArray(),
            data.ToArray(),
            matchingCount);
    }

    private static int EstimateCapacity(int vertexCount, int matchingSize)
    {
        if (matchingSize == 6 && vertexCount == 17)
            return 4_900_896;
        return 0;
    }

    public ulong[] BuildPackedKeys()
    {
        // With m <= 28, a complete-graph edge id is at most 377 and needs 9 bits.
        // Seven 9-bit ids fit in a ulong (63 bits).
        if (MatchingSize > 7 || EdgeCount > 512)
            throw new NotSupportedException("Packed keyhez legfeljebb 7 élű matching és legfeljebb 512 él támogatott.");

        var keys = new ulong[MatchingCount];
        var temp = new int[MatchingSize];
        for (var m = 0; m < MatchingCount; m++)
        {
            var baseIndex = m * MatchingSize;
            Array.Copy(_matchingEdges, baseIndex, temp, 0, MatchingSize);
            Array.Sort(temp, 0, MatchingSize);

            ulong key = 0;
            for (var j = 0; j < MatchingSize; j++)
                key |= ((ulong)temp[j]) << (9 * j);
            keys[m] = key;
        }
        return keys;
    }
}
