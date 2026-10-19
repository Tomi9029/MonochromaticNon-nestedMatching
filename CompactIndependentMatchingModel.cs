namespace OrderedMatching;

/// <summary>
/// A CompactMatchingModeltől szándékosan eltérő generátor.
/// Előbb kiválasztja a 2n csúcsot, majd azokon generál tökéletes párosításokat,
/// végül kiszűri az ölelő párokat. Csak lapos tömböket tárol.
/// </summary>
internal sealed class CompactIndependentMatchingModel
{
    private readonly int[] _matchingEdges;
    private readonly int[,] _edgeIds;
    private readonly (int U, int V)[] _edges;

    public int VertexCount { get; }
    public int MatchingSize { get; }
    public int EdgeCount => _edges.Length;
    public int MatchingCount { get; }

    private CompactIndependentMatchingModel(
        int vertexCount,
        int matchingSize,
        (int U, int V)[] edges,
        int[,] edgeIds,
        int[] matchingEdges,
        int matchingCount)
    {
        VertexCount = vertexCount;
        MatchingSize = matchingSize;
        _edges = edges;
        _edgeIds = edgeIds;
        _matchingEdges = matchingEdges;
        MatchingCount = matchingCount;
    }

    public ReadOnlySpan<int> Matching(int matchingId)
        => _matchingEdges.AsSpan(matchingId * MatchingSize, MatchingSize);

    public static CompactIndependentMatchingModel Build(int vertexCount, int matchingSize)
    {
        if (vertexCount < 0 || matchingSize < 0 || 2 * matchingSize > vertexCount)
            throw new ArgumentOutOfRangeException();

        var edges = new List<(int U, int V)>(vertexCount * (vertexCount - 1) / 2);
        var edgeIds = new int[vertexCount, vertexCount];
        for (var i = 0; i < vertexCount; i++)
        for (var j = i + 1; j < vertexCount; j++)
        {
            edgeIds[i, j] = edges.Count;
            edges.Add((i, j));
        }

        var data = new List<int>(vertexCount == 17 && matchingSize == 6 ? 4_900_896 : 0);
        var chosen = new int[2 * matchingSize];
        var used = new bool[2 * matchingSize];
        var current = new int[matchingSize];
        var count = 0;

        void GeneratePerfect(int depth)
        {
            if (depth == matchingSize)
            {
                for (var i = 0; i < matchingSize; i++)
                for (var j = i + 1; j < matchingSize; j++)
                {
                    var a = edges[current[i]];
                    var b = edges[current[j]];
                    if (NestedRelations.IsNested(a, b))
                        return;
                }

                for (var i = 0; i < matchingSize; i++)
                    data.Add(current[i]);
                count++;
                return;
            }

            var first = -1;
            for (var i = 0; i < chosen.Length; i++)
            {
                if (!used[i])
                {
                    first = i;
                    break;
                }
            }
            if (first < 0) return;

            used[first] = true;
            for (var second = first + 1; second < chosen.Length; second++)
            {
                if (used[second]) continue;
                used[second] = true;
                current[depth] = edgeIds[
                    Math.Min(chosen[first], chosen[second]),
                    Math.Max(chosen[first], chosen[second])];
                GeneratePerfect(depth + 1);
                used[second] = false;
            }
            used[first] = false;
        }

        void ChooseVertices(int next, int depth)
        {
            if (depth == chosen.Length)
            {
                Array.Clear(used);
                GeneratePerfect(0);
                return;
            }

            var need = chosen.Length - depth;
            for (var v = next; v <= vertexCount - need; v++)
            {
                chosen[depth] = v;
                ChooseVertices(v + 1, depth + 1);
            }
        }

        if (matchingSize > 0)
            ChooseVertices(0, 0);

        return new CompactIndependentMatchingModel(
            vertexCount,
            matchingSize,
            edges.ToArray(),
            edgeIds,
            data.ToArray(),
            count);
    }

    public ulong[] BuildPackedKeys()
    {
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
