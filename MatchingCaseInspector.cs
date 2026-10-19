using System.Numerics;

namespace OrderedMatching;

/// <summary>
/// Prints one generated matching by its 1-based index, along with the vertex set,
/// selected edges, unused vertices and all/requested red-blue assignments on those edges.
/// It does not pretend that a matching is a complete coloring of every edge of K_m.
/// </summary>
internal static class MatchingCaseInspector
{
    public static int Run(int vertexCount, int matchingSize, long caseIndex, bool forbidNested, int maxColorings)
    {
        if (matchingSize < 1)
        {
            Console.Error.WriteLine("n (a matching éleinek száma) pozitív egész kell legyen.");
            return 2;
        }
        if (vertexCount < 0)
        {
            Console.Error.WriteLine("m (a csúcsok száma) nem lehet negatív.");
            return 2;
        }
        if (vertexCount > 28)
        {
            Console.Error.WriteLine("A --case részletes nézete jelenleg legfeljebb m=28 csúcsot támogat.");
            return 2;
        }
        if (caseIndex < 1)
        {
            Console.Error.WriteLine("A --case indexelése 1-től indul; adj meg pozitív egészet.");
            return 2;
        }
        if (maxColorings < 1)
        {
            Console.Error.WriteLine("A --max-colorings értékének pozitívnak kell lennie.");
            return 2;
        }

        var total = forbidNested
            ? MatchingCountMath.CountNonNested(vertexCount, matchingSize)
            : MatchingCountMath.CountAll(vertexCount, matchingSize);

        Console.WriteLine("=== Matching eset részletes nézete ===");
        Console.WriteLine($"n (matching élei): {matchingSize}");
        Console.WriteLine($"m (gráf csúcsai): {vertexCount}");
        Console.WriteLine($"Ölelő élek tiltva: {forbidNested}");
        Console.WriteLine($"Eset sorszáma: {caseIndex:N0} / {total:N0} (1-alapú index)");
        Console.WriteLine();

        if (total.IsZero)
        {
            Console.WriteLine($"Nincs {matchingSize} élű matching {vertexCount} csúcson, mert 2n > m.");
            return 2;
        }
        if (new BigInteger(caseIndex) > total)
        {
            Console.Error.WriteLine($"A kért esetindex túl nagy. A megengedett tartomány: 1..{total}.");
            return 2;
        }

        var matching = FindMatchingAtIndex(vertexCount, matchingSize, caseIndex - 1, forbidNested);
        if (matching is null)
        {
            Console.Error.WriteLine("Belső hiba: a matching-generátor nem találta meg a kért indexet.");
            return 1;
        }

        Console.WriteLine("A teljes gráf csúcsai:");
        Console.WriteLine("  " + string.Join(" ", Enumerable.Range(1, vertexCount)));

        var usedVertices = matching.SelectMany(e => new[] { e.U, e.V }).Distinct().OrderBy(v => v).ToArray();
        var usedSet = usedVertices.ToHashSet();
        var unusedVertices = Enumerable.Range(0, vertexCount).Where(v => !usedSet.Contains(v)).Select(v => v + 1);

        Console.WriteLine("A kiválasztott matching csúcsai:");
        Console.WriteLine("  " + string.Join(" ", usedVertices.Select(v => v + 1)));
        Console.WriteLine("Matchingben nem szereplő csúcsok:");
        Console.WriteLine("  " + (unusedVertices.Any() ? string.Join(" ", unusedVertices) : "(nincs)"));
        Console.WriteLine();

        Console.WriteLine("A kiválasztott M_n élei (a csúcsszámok 1-től indulnak):");
        for (var i = 0; i < matching.Length; i++)
        {
            var (u, v) = matching[i];
            var edgeId = u * (2 * vertexCount - u - 1) / 2 + (v - u - 1) + 1;
            Console.WriteLine($"  e{i + 1}: ({u + 1},{v + 1})   [K_m élindex: {edgeId}]");
        }
        Console.WriteLine();

        // m <= 28 means n <= 14 for a matching that exists; 2^14 fits comfortably in a long.
        var allColorings = 1L << matchingSize;
        var toPrint = (int)Math.Min(allColorings, maxColorings);
        Console.WriteLine($"A matching éleinek lehetséges piros/kék színezései: {allColorings:N0}");
        Console.WriteLine($"Most kiírt színezések: {toPrint:N0}");
        Console.WriteLine("Jelölés: P = piros, K = kék. A színek sorrendje e1, e2, ..., en.");

        for (var mask = 0; mask < toPrint; mask++)
        {
            var colors = new char[matchingSize];
            var assignments = new string[matchingSize];
            for (var j = 0; j < matchingSize; j++)
            {
                var isRed = (mask & (1 << j)) != 0;
                colors[j] = isRed ? 'P' : 'K';
                assignments[j] = $"({matching[j].U + 1},{matching[j].V + 1})={(isRed ? "piros" : "kék")}";
            }
            Console.WriteLine($"  {mask + 1,5}: {new string(colors)}   {string.Join("; ", assignments)}");
        }

        if (toPrint < allColorings)
            Console.WriteLine($"... a lista csonkolva. Az összes kiírásához használd: --max-colorings {allColorings}.");

        Console.WriteLine();
        Console.WriteLine("Fontos: itt a matching n élének színezéseit listázzuk, nem a teljes K_m minden élének színezését.");
        return 0;
    }

    internal static (int U, int V)[]? FindMatchingAtIndex(int m, int n, long zeroBasedIndex, bool forbidNested)
    {
        var used = new bool[m];
        var current = new (int U, int V)[n];
        (int U, int V)[]? found = null;
        long seen = 0;

        void Generate(int v, int depth)
        {
            if (found is not null) return;
            if (depth == n)
            {
                if (seen == zeroBasedIndex)
                {
                    found = (ValueTuple<int, int>[])current.Clone();
                    return;
                }
                seen++;
                return;
            }
            if (v >= m) return;

            var free = 0;
            for (var i = v; i < m; i++)
                if (!used[i]) free++;
            if (free < 2 * (n - depth)) return;

            if (used[v])
            {
                Generate(v + 1, depth);
                return;
            }

            // Same deterministic order as CompactMatchingModel: skip v, then pair v with w>v.
            used[v] = true;
            Generate(v + 1, depth);
            used[v] = false;
            if (found is not null) return;

            used[v] = true;
            for (var w = v + 1; w < m; w++)
            {
                if (used[w]) continue;
                var candidate = (U: v, V: w);
                if (forbidNested)
                {
                    var valid = true;
                    for (var j = 0; j < depth; j++)
                    {
                        if (!NestedRelations.IsNested(candidate, current[j])) continue;
                        valid = false;
                        break;
                    }
                    if (!valid) continue;
                }

                used[w] = true;
                current[depth] = candidate;
                Generate(v + 1, depth + 1);
                used[w] = false;
                if (found is not null) break;
            }
            used[v] = false;
        }

        Generate(0, 0);
        return found;
    }
}
