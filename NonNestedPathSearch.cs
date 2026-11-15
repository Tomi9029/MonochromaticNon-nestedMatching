using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace OrderedMatching;

internal sealed record NonNestedPath(int[] EdgeIds)
{
    public int Length => EdgeIds.Length;
}

internal sealed record MonochromaticPathWitness(bool IsRed, int[] Vertices)
{
    public int EdgeLength => Math.Max(0, Vertices.Length - 1);
    public string ColorName => IsRed ? "piros" : "kék";
}

internal static class NonNestedPathGenerator
{
    public static List<NonNestedPath> Generate(int vertexCount, int edgeLength)
    {
        if (vertexCount < 0 || edgeLength < 1 || edgeLength >= vertexCount)
            return new List<NonNestedPath>();

        var result = new List<NonNestedPath>();
        var vertices = new int[edgeLength + 1];
        var edgeIds = new int[edgeLength];
        var used = new bool[vertexCount];

        void Dfs(int depth, int last)
        {
            if (depth == edgeLength)
            {
                // A path and its reverse are the same undirected path.
                if (vertices[0] >= vertices[edgeLength]) return;
                // The path sequence is needed only while generating the path.
                // The SAT/exhaustive consumers need its edge set only, so keep
                // just the edge ids to reduce memory for K_12 and K_13.
                var edgeCopy = (int[])edgeIds.Clone();
                result.Add(new NonNestedPath(edgeCopy));
                return;
            }

            for (var next = 0; next < vertexCount; next++)
            {
                if (used[next]) continue;
                var newEdge = (U: Math.Min(last, next), V: Math.Max(last, next));
                var valid = true;
                for (var i = 0; i < depth; i++)
                {
                    var old = (U: Math.Min(vertices[i], vertices[i + 1]), V: Math.Max(vertices[i], vertices[i + 1]));
                    if (NestedRelations.IsNested(newEdge, old))
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid) continue;

                edgeIds[depth] = EdgeId(vertexCount, newEdge.U, newEdge.V);
                vertices[depth + 1] = next;
                used[next] = true;
                Dfs(depth + 1, next);
                used[next] = false;
            }
        }

        for (var start = 0; start < vertexCount; start++)
        {
            Array.Clear(used);
            used[start] = true;
            vertices[0] = start;
            Dfs(0, start);
        }

        return result;
    }

    public static int EdgeId(int vertexCount, int u, int v)
    {
        if (u > v) (u, v) = (v, u);
        return u * (2 * vertexCount - u - 1) / 2 + (v - u - 1);
    }
}

internal sealed record PathExhaustiveResult(
    int VertexCount,
    long TotalColorings,
    int MinimumLongestPath,
    long WorstColoringCount,
    long[] Distribution,
    bool[] WorstColoring,
    TimeSpan Elapsed);

internal static class PathExtremalSearch
{
    // Literal enumeration is intentionally capped at seven vertices (2^21 colorings).
    // Above that, an equivalent SAT formulation searches for a coloring avoiding
    // a path of the target length, which exactly decides the min-max quantity.
    public static int Run(int vertexCount, bool force, bool showColoring, string? outputFile)
    {
        if (vertexCount < 2)
        {
            Console.Error.WriteLine("A path-vizsgálathoz legalább 2 csúcs kell.");
            return 2;
        }
        if (vertexCount <= 7)
            return RunExhaustive(vertexCount, showColoring, outputFile);

        // The current explicit path-family + DPLL implementation is sized for
        // K_8..K_13. K_12 and K_13 can create hundreds of thousands of path
        // constraints, so require an explicit opt-in before allocating them.
        const int defaultSatMaximum = 11;
        const int forceSatMaximum = 13;
        if (vertexCount > forceSatMaximum)
        {
            Console.Error.WriteLine($"A path mód jelenlegi explicit útgenerátora legfeljebb K_{forceSatMaximum}-ig támogatott.");
            Console.Error.WriteLine("Nagyobb gráfhoz már streaming útgenerálás / más SAT backend szükséges.");
            return 2;
        }
        if (vertexCount > defaultSatMaximum && !force)
        {
            Console.Error.WriteLine($"K_{vertexCount} esetén az útcsalád és a SAT-formula nagy memóriát igényelhet.");
            Console.Error.WriteLine($"A K_{vertexCount} futtatásához add meg a --force kapcsolót (jelenlegi felső határ: K_{forceSatMaximum}).");
            return 2;
        }
        if (vertexCount > 11)
        {
            Console.WriteLine("FIGYELEM: nagy útcsalád készül; a memóriaigény és a SAT-keresési idő jelentős lehet.");
            Console.WriteLine("A futás pontos eredményt ad, ha befejeződik; időkorlátot nem alkalmaz.");
            Console.WriteLine();
        }
        return RunSatMinMax(vertexCount, showColoring, outputFile);
    }

    private static int RunExhaustive(int m, bool showColoring, string? outputFile)
    {
        var pathCounts = new int[m];
        for (var k = 1; k < m; k++)
            pathCounts[k] = NonNestedPathGenerator.Generate(m, k).Count;

        Console.WriteLine("=== Non-nested monochromatic path extremal search ===");
        Console.WriteLine($"Rendezett teljes gráf: K_{m}");
        Console.WriteLine($"Élek: {m * (m - 1) / 2}; összes kétszínezés: {(1UL << (m * (m - 1) / 2)):N0}");
        Console.WriteLine("Módszer: az összes címkézett piros/kék színezés szó szerinti végigvizsgálása.");
        Console.WriteLine();
        for (var k = 1; k < m; k++)
            Console.WriteLine($"Nem-nested, {k} élű utak: {pathCounts[k]:N0}");

        var result = CalculateExhaustive(m);
        var witness = FindLongestPath(m, result.WorstColoring);
        if (witness.EdgeLength != result.MinimumLongestPath)
            return Fail("A közvetlen útellenőrző nem egyezik az enumerációs eredménnyel.");

        Console.WriteLine();
        Console.WriteLine($"Végigvizsgált színezések: {result.TotalColorings:N0}");
        Console.WriteLine("Leghosszabb monochromatikus non-nested út hosszának eloszlása (élben):");
        for (var k = 0; k < result.Distribution.Length; k++)
            if (result.Distribution[k] != 0)
                Console.WriteLine($"  L={k}: {result.Distribution[k]:N0} színezés");
        Console.WriteLine();
        Console.WriteLine($"Legrosszabb eset: L_min={result.MinimumLongestPath} él ({result.MinimumLongestPath + 1} csúcs)");
        Console.WriteLine($"Ilyen színezések száma: {result.WorstColoringCount:N0}");
        PrintWitness(witness);
        Console.WriteLine($"Idő: {result.Elapsed}");
        Console.WriteLine("EXHAUSTIVE VERIFICATION: PASS");

        EmitColoring(m, result.WorstColoring, showColoring, outputFile);
        return 0;
    }

    internal static PathExhaustiveResult CalculateExhaustive(int m)
    {
        if (m < 2 || m > 7)
            throw new ArgumentOutOfRangeException(nameof(m), "A szó szerinti enumeráció 2..7 csúcsot támogat.");

        var edgeCount = m * (m - 1) / 2;
        var coloringCount = 1UL << edgeCount;
        // Literal enumeration is restricted to m<=7, so every edge id fits
        // safely in a 64-bit mask. The general path generator itself no longer
        // stores a truncated ulong mask; SAT mode uses the full int edge ids.
        var pathsByLength = new List<(NonNestedPath Path, ulong EdgeMask)>[m];
        for (var k = 1; k < m; k++)
        {
            pathsByLength[k] = new List<(NonNestedPath Path, ulong EdgeMask)>();
            foreach (var path in NonNestedPathGenerator.Generate(m, k))
            {
                ulong mask = 0;
                foreach (var edgeId in path.EdgeIds)
                {
                    if ((uint)edgeId >= 64)
                        throw new InvalidOperationException("A 64 bites enumerációs maszkhoz túl nagy élindex került.");
                    mask |= 1UL << edgeId;
                }
                pathsByLength[k].Add((path, mask));
            }
        }

        var distribution = new long[m];
        var worstLength = m;
        var worstColoringCount = 0L;
        ulong worstMask = 0;
        var sw = Stopwatch.StartNew();

        for (ulong coloring = 0; coloring < coloringCount; coloring++)
        {
            var longest = 0;
            for (var k = m - 1; k >= 1 && longest == 0; k--)
            {
                foreach (var item in pathsByLength[k])
                {
                    var redMask = coloring & item.EdgeMask;
                    if (redMask == 0 || redMask == item.EdgeMask)
                    {
                        longest = k;
                        break;
                    }
                }
            }

            distribution[longest]++;
            if (longest < worstLength)
            {
                worstLength = longest;
                worstColoringCount = 1;
                worstMask = coloring;
            }
            else if (longest == worstLength)
            {
                worstColoringCount++;
            }
        }
        sw.Stop();

        return new PathExhaustiveResult(
            m,
            checked((long)coloringCount),
            worstLength,
            worstColoringCount,
            distribution,
            MaskToColoring(m, worstMask),
            sw.Elapsed);
    }

    private static int RunSatMinMax(int m, bool showColoring, string? outputFile)
    {
        var edgeCount = m * (m - 1) / 2;
        var totalColorings = BigInteger.One << edgeCount;
        Console.WriteLine("=== Non-nested monochromatic path extremal search ===");
        Console.WriteLine($"Rendezett teljes gráf: K_{m}");
        Console.WriteLine($"Élek: {edgeCount}; címkézett kétszínezések: {totalColorings:N0}");
        Console.WriteLine("Módszer: SAT-alapú univerzális ellenőrzés.");
        Console.WriteLine("Egy k élű út minden színezési feltételét úgy kódoljuk, hogy az út ne lehessen egyszínű.");
        Console.WriteLine("UNSAT esetén minden színezés tartalmaz egyszínű, non-nested k-utat.");
        Console.WriteLine();

        bool[]? lastAvoidingColoring = null;
        var totalSw = Stopwatch.StartNew();

        for (var k = m - 1; k >= 1; k--)
        {
            Console.WriteLine($"k={k}: a non-nested utak generálása...");
            var buildSw = Stopwatch.StartNew();
            var paths = NonNestedPathGenerator.Generate(m, k);
            var edgeSets = new int[paths.Count][];
            for (var i = 0; i < paths.Count; i++)
                edgeSets[i] = paths[i].EdgeIds;
            var pathCount = paths.Count;
            var instance = CnfInstance.BuildFromForbiddenEdgeSets(m, k, edgeSets);
            // The CNF now owns a compact flat literal array. Release the
            // generated path objects before constructing the DPLL watch lists.
            Array.Clear(edgeSets);
            paths.Clear();
            GC.Collect();
            buildSw.Stop();

            var solveSw = Stopwatch.StartNew();
            var solver = new DpllSolver(instance);
            var fixedOk = edgeCount == 0 || solver.FixVariable(0, true);
            var hasAvoidingColoring = fixedOk && solver.Solve();
            solveSw.Stop();
            Console.WriteLine($"k={k}: utak={pathCount:N0}, klauzulák={instance.ClauseCount:N0}, " +
                              $"avoid-coloring={(hasAvoidingColoring ? "SAT" : "UNSAT")}, " +
                              $"decisions={solver.DecisionNodes:N0}, build={buildSw.Elapsed}, solve={solveSw.Elapsed}");

            if (hasAvoidingColoring)
            {
                lastAvoidingColoring = solver.GetAssignment();
                continue;
            }

            var worstColoring = lastAvoidingColoring ?? Enumerable.Repeat(true, edgeCount).ToArray();
            var witness = FindLongestPath(m, worstColoring);
            if (witness.EdgeLength != k)
                return Fail($"A SAT-tanú közvetlen útellenőrzése {witness.EdgeLength} élt adott, de az elvárt érték {k}.");

            totalSw.Stop();
            Console.WriteLine();
            Console.WriteLine($"Legrosszabb eset: L_min={k} él ({k + 1} csúcs)");
            Console.WriteLine("Érvelés: a k-path elkerülő formula UNSAT, a k+1-path elkerülő formula SAT.");
            PrintWitness(witness);
            Console.WriteLine($"Teljes idő: {totalSw.Elapsed}");
            Console.WriteLine("SAT MIN-MAX VERIFICATION: PASS");
            EmitColoring(m, worstColoring, showColoring, outputFile);
            return 0;
        }

        return Fail("Váratlan: még a k=1 út elkerülő formulája is SAT lett.");
    }

    public static MonochromaticPathWitness FindLongestPath(int m, bool[] red)
    {
        if (red.Length != m * (m - 1) / 2)
            throw new ArgumentException("Hibás élszínezés-hossz.", nameof(red));

        var visited = new bool[m];
        var pathVertices = new int[m];
        var bestVertices = new[] { 0 };
        var bestRed = true;
        var bestLength = 0;

        for (var color = 0; color <= 1; color++)
        for (var start = 0; start < m; start++)
        {
            Array.Clear(visited);
            visited[start] = true;
            pathVertices[0] = start;
            Dfs(start, 0, color == 1);
        }

        return new MonochromaticPathWitness(bestRed, bestVertices);

        void Dfs(int current, int depth, bool wantRed)
        {
            if (depth > bestLength)
            {
                bestLength = depth;
                bestRed = wantRed;
                bestVertices = pathVertices.Take(depth + 1).ToArray();
            }

            if (depth == m - 1) return;
            for (var next = 0; next < m; next++)
            {
                if (visited[next]) continue;
                var edgeId = NonNestedPathGenerator.EdgeId(m, current, next);
                if (red[edgeId] != wantRed) continue;

                var newEdge = (U: Math.Min(current, next), V: Math.Max(current, next));
                var valid = true;
                for (var i = 0; i < depth; i++)
                {
                    var old = (U: Math.Min(pathVertices[i], pathVertices[i + 1]),
                               V: Math.Max(pathVertices[i], pathVertices[i + 1]));
                    if (NestedRelations.IsNested(newEdge, old))
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid) continue;

                visited[next] = true;
                pathVertices[depth + 1] = next;
                Dfs(next, depth + 1, wantRed);
                visited[next] = false;
            }
        }
    }

    private static bool[] MaskToColoring(int m, ulong mask)
    {
        var count = m * (m - 1) / 2;
        var result = new bool[count];
        for (var e = 0; e < count; e++) result[e] = (mask & (1UL << e)) != 0;
        return result;
    }

    private static void PrintWitness(MonochromaticPathWitness witness)
    {
        var path = string.Join(" -> ", witness.Vertices.Select(v => v + 1));
        var edges = new List<string>();
        for (var i = 0; i + 1 < witness.Vertices.Length; i++)
        {
            var u = witness.Vertices[i] + 1;
            var v = witness.Vertices[i + 1] + 1;
            edges.Add($"({Math.Min(u, v)},{Math.Max(u, v)})");
        }
        Console.WriteLine($"Leghosszabb út tanúja: {witness.ColorName}, {witness.EdgeLength} él: {path}");
        Console.WriteLine($"Útélek: {string.Join(", ", edges)}");
    }

    private static void EmitColoring(int m, bool[] red, bool show, string? file)
    {
        if (!show && file is null) return;
        var sb = new StringBuilder();
        sb.AppendLine($"m={m}");
        sb.AppendLine("PIROS:");
        var edge = 0;
        for (var u = 0; u < m; u++)
        for (var v = u + 1; v < m; v++, edge++)
            if (red[edge]) sb.AppendLine($"({u + 1},{v + 1})");
        sb.AppendLine("KÉK:");
        edge = 0;
        for (var u = 0; u < m; u++)
        for (var v = u + 1; v < m; v++, edge++)
            if (!red[edge]) sb.AppendLine($"({u + 1},{v + 1})");
        var text = sb.ToString();
        if (show) Console.WriteLine(text);
        if (file is not null)
        {
            File.WriteAllText(file, text);
            Console.WriteLine($"A legrosszabb színezést ide mentettem: {Path.GetFullPath(file)}");
        }
    }

    private static int Fail(string message)
    {
        Console.WriteLine($"FAIL: {message}");
        return 1;
    }
}
