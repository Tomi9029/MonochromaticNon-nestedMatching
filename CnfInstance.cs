namespace OrderedMatching;

internal sealed class CnfInstance
{
    private readonly (int U, int V)[] _edges;
    private readonly int[] _clauseLiterals;

    public int VertexCount { get; }
    public int EdgeCount => _edges.Length;
    public IReadOnlyList<(int U, int V)> Edges => _edges;
    public int MatchingSize { get; }
    public int MatchingsGenerated { get; private set; }
    public int ClauseCount { get; private set; }

    private CnfInstance(int vertexCount, int matchingSize, (int U, int V)[] edges, int[] clauseLiterals)
    {
        VertexCount = vertexCount;
        MatchingSize = matchingSize;
        _edges = edges;
        _clauseLiterals = clauseLiterals;
    }

    public ReadOnlySpan<int> GetClause(int clauseId)
        => _clauseLiterals.AsSpan(clauseId * MatchingSize, MatchingSize);

    public (int U, int V) GetEdge(int edgeId) => _edges[edgeId];

    public int GetEdgeId(int u, int v)
    {
        if (u > v) (u, v) = (v, u);
        if (u < 0 || v >= VertexCount || u >= v)
            throw new ArgumentOutOfRangeException();
        return u * (2 * VertexCount - u - 1) / 2 + (v - u - 1);
    }

    public IEnumerable<string> GetColoredEdges(bool[] assignment, bool red)
    {
        for (var e = 0; e < _edges.Length; e++)
        {
            if (assignment[e] == red)
                yield return $"({_edges[e].U + 1},{_edges[e].V + 1})";
        }
    }

    public string FormatColoring(bool[] assignment)
    {
        var lines = new List<string>
        {
            $"n={MatchingSize}",
            $"m={VertexCount}",
            ""
        };
        lines.Add("PIROS:");
        lines.AddRange(GetColoredEdges(assignment, true));
        lines.Add("");
        lines.Add("KÉK:");
        lines.AddRange(GetColoredEdges(assignment, false));
        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }

    public static CnfInstance Build(int vertexCount, int matchingSize, bool forbidNested)
    {
        var edges = new List<(int U, int V)>();
        var edgeId = new int[vertexCount, vertexCount];
        for (var i = 0; i < vertexCount; i++)
        for (var j = i + 1; j < vertexCount; j++)
        {
            edgeId[i, j] = edges.Count;
            edges.Add((i, j));
        }

        var clauseData = new List<int>();
        var selectedEdges = new int[matchingSize];
        var used = new bool[vertexCount];
        var matchings = 0;

        void AddCurrentMatching()
        {
            matchings++;

            // x_e = true  <=> piros
            // A matching nem lehet teljesen kék: x_e1 OR ... OR x_en.
            for (var i = 0; i < matchingSize; i++)
                clauseData.Add(2 * selectedEdges[i]);

            // A matching nem lehet teljesen piros: !x_e1 OR ... OR !x_en.
            for (var i = 0; i < matchingSize; i++)
                clauseData.Add(2 * selectedEdges[i] + 1);
        }

        void Generate(int v, int depth)
        {
            if (depth == matchingSize)
            {
                AddCurrentMatching();
                return;
            }

            if (v >= vertexCount)
                return;

            var unused = 0;
            for (var i = v; i < vertexCount; i++)
                if (!used[i]) unused++;
            if (unused < 2 * (matchingSize - depth))
                return;

            if (used[v])
            {
                Generate(v + 1, depth);
                return;
            }

            // v kimarad a matchingből.
            used[v] = true;
            Generate(v + 1, depth);
            used[v] = false;

            // v bekerül a matchingbe; párba állítjuk egy későbbi w-vel.
            used[v] = true;
            for (var w = v + 1; w < vertexCount; w++)
            {
                if (used[w])
                    continue;

                var newEdge = (v, w);
                var valid = true;

                if (forbidNested)
                {
                    for (var k = 0; k < depth; k++)
                    {
                        var oldEdge = edges[selectedEdges[k]];
                        if (NestedRelations.IsNested(newEdge, oldEdge))
                        {
                            valid = false;
                            break;
                        }
                    }
                }

                if (!valid)
                    continue;

                used[w] = true;
                selectedEdges[depth] = edgeId[v, w];
                Generate(v + 1, depth + 1);
                used[w] = false;
            }
            used[v] = false;
        }

        Generate(0, 0);
        var instance = new CnfInstance(vertexCount, matchingSize, edges.ToArray(), clauseData.ToArray())
        {
            MatchingsGenerated = matchings,
            ClauseCount = matchings * 2
        };
        return instance;
    }

}
