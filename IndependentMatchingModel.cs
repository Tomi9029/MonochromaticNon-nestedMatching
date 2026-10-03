namespace OrderedMatching;

/// <summary>
/// Független, a CnfInstance.Build-től külön megírt modell.
/// Először kiválasztja a 2n csúcsot, majd azokon az összes tökéletes párosítást generálja,
/// végül kiszűri az ölelő (nested) párokat. A cél a CNF-generálás független ellenőrzése.
/// </summary>
internal sealed record IndependentMatching(int[] EdgeIds);

internal sealed class IndependentMatchingModel
{
    private readonly (int U, int V)[] _edges;
    private readonly int[,] _edgeIds;
    private readonly List<IndependentMatching> _matchings;

    public int VertexCount { get; }
    public int MatchingSize { get; }
    public int EdgeCount => _edges.Length;
    public IReadOnlyList<(int U, int V)> Edges => _edges;
    public IReadOnlyList<IndependentMatching> Matchings => _matchings;

    private IndependentMatchingModel(
        int vertexCount,
        int matchingSize,
        (int U, int V)[] edges,
        int[,] edgeIds,
        List<IndependentMatching> matchings)
    {
        VertexCount = vertexCount;
        MatchingSize = matchingSize;
        _edges = edges;
        _edgeIds = edgeIds;
        _matchings = matchings;
    }

    public int GetEdgeId(int u, int v)
    {
        if (u > v) (u, v) = (v, u);
        return _edgeIds[u, v];
    }

    public static IndependentMatchingModel Build(int vertexCount, int matchingSize, bool forbidNested = true)
    {
        if (vertexCount < 0 || matchingSize < 0 || 2 * matchingSize > vertexCount)
            throw new ArgumentOutOfRangeException();

        var edges = new List<(int U, int V)>();
        var edgeIds = new int[vertexCount, vertexCount];
        for (var i = 0; i < vertexCount; i++)
        for (var j = i + 1; j < vertexCount; j++)
        {
            edgeIds[i, j] = edges.Count;
            edges.Add((i, j));
        }

        var result = new List<IndependentMatching>();
        var chosen = new int[2 * matchingSize];
        var used = new bool[vertexCount];
        var current = new int[matchingSize];

        void GeneratePerfect(int depth)
        {
            if (depth == matchingSize)
            {
                var pairs = new (int U, int V)[matchingSize];
                for (var i = 0; i < matchingSize; i++)
                    pairs[i] = edges[current[i]];

                if (forbidNested)
                {
                    for (var i = 0; i < pairs.Length; i++)
                    for (var j = i + 1; j < pairs.Length; j++)
                        if (IsNestedIndependent(pairs[i], pairs[j]))
                            return;
                }

                var edgeIdsCopy = new int[matchingSize];
                Array.Copy(current, edgeIdsCopy, matchingSize);
                result.Add(new IndependentMatching(edgeIdsCopy));
                return;
            }

            var first = -1;
            for (var i = 0; i < chosen.Length; i++)
            {
                if (!used[chosen[i]])
                {
                    first = chosen[i];
                    break;
                }
            }
            if (first < 0) return;

            used[first] = true;
            foreach (var second in chosen)
            {
                if (used[second]) continue;
                used[second] = true;
                current[depth] = edgeIds[Math.Min(first, second), Math.Max(first, second)];
                GeneratePerfect(depth + 1);
                used[second] = false;
            }
            used[first] = false;
        }

        void ChooseVertices(int next, int count)
        {
            if (count == chosen.Length)
            {
                Array.Clear(used);
                GeneratePerfect(0);
                return;
            }

            var need = chosen.Length - count;
            for (var v = next; v <= vertexCount - need; v++)
            {
                chosen[count] = v;
                ChooseVertices(v + 1, count + 1);
            }
        }

        if (matchingSize == 0)
        {
            result.Add(new IndependentMatching(Array.Empty<int>()));
        }
        else
        {
            ChooseVertices(0, 0);
        }

        return new IndependentMatchingModel(vertexCount, matchingSize, edges.ToArray(), edgeIds, result);
    }

    private static bool IsNestedIndependent((int U, int V) a, (int U, int V) b)
    {
        var a1 = Math.Min(a.U, a.V);
        var a2 = Math.Max(a.U, a.V);
        var b1 = Math.Min(b.U, b.V);
        var b2 = Math.Max(b.U, b.V);
        if (a1 > b1)
            (a1, a2, b1, b2) = (b1, b2, a1, a2);
        return a1 < b1 && b2 < a2;
    }
}
