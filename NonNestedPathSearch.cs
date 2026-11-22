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
                // just the edge ids to keep the path representation compact at larger graph sizes.
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

internal sealed record PathAvoidanceResult(
    bool HasAvoidingColoring,
    bool[]? Coloring,
    long LearnedPathCount,
    long SolverIterations,
    long DecisionNodes,
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

        // Preserve the pre-v26 explicit SAT method through K_13. For larger
        // graphs, use lazy cut generation so the complete path family is never
        // materialized. K_20 is the experimental ceiling for this release.
        const int defaultSatMaximum = 11;
        const int forceSatMaximum = 20;
        if (vertexCount > forceSatMaximum)
        {
            Console.Error.WriteLine($"A path mód jelenlegi lazy SAT-keresése legfeljebb K_{forceSatMaximum}-ig támogatott.");
            Console.Error.WriteLine("A K_21+ méretekhez további optimalizálás szükséges.");
            return 2;
        }
        if (vertexCount > defaultSatMaximum && !force)
        {
            Console.Error.WriteLine($"K_{vertexCount} esetén a lazy SAT-keresés jelentős CPU-időt igényelhet.");
            Console.Error.WriteLine($"A K_{vertexCount} futtatásához add meg a --force kapcsolót (jelenlegi felső határ: K_{forceSatMaximum}).");
            return 2;
        }
        if (vertexCount <= 13)
        {
            Console.WriteLine("Módszer: explicit SAT-formula a teljes non-nested útcsaládból.");
            if (vertexCount > 11)
                Console.WriteLine("FIGYELEM: K_12 és K_13 esetén a teljes útcsalád memóriát és időt igényel; ez a korábbi implementációs útvonal.");
        }
        else
        {
            Console.WriteLine("FIGYELEM: a teljes útcsaládot nem tároljuk el.");
            Console.WriteLine("A SAT-solver csak a talált egyszínű non-nested utakból tanul új korlátozásokat.");
            Console.WriteLine($"A lazy keresés pontos, de K_14..K_{forceSatMaximum} esetén hosszú ideig tarthat.");
        }
        Console.WriteLine();
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
        if (m <= 13)
        {
            Console.WriteLine("Módszer: explicit SAT + a teljes non-nested útcsalád előzetes generálása.");
        }
        else
        {
            Console.WriteLine("Módszer: lazy SAT + ellenpélda-út alapú vágás (cut generation).");
            Console.WriteLine("A teljes non-nested útcsaládot nem generáljuk előre.");
            Console.WriteLine("Minden SAT-jelöltben megkeressük az egyszínű non-nested k-utat; csak a talált utak tiltó feltételeit adjuk a CNF-hez.");
            Console.WriteLine("Ha a tanult részformula UNSAT, akkor a teljes útformula is UNSAT.");
        }
        Console.WriteLine();

        bool[]? lastAvoidingColoring = null;
        var totalSw = Stopwatch.StartNew();

        for (var k = m - 1; k >= 1; k--)
        {
            var useLazyCuts = m > 13;
            Console.WriteLine(useLazyCuts
                ? $"k={k}: lazy SAT-keresés indul..."
                : $"k={k}: explicit SAT-keresés indul...");
            var result = useLazyCuts
                ? SolveAvoidingPathsLazy(m, k, showProgress: true)
                : SolveAvoidingPathsExplicit(m, k);
            Console.WriteLine($"k={k}: {(useLazyCuts ? "tanult utak" : "generált utak")}={result.LearnedPathCount:N0}, " +
                              $"SAT-iterációk={result.SolverIterations:N0}, " +
                              $"avoid-coloring={(result.HasAvoidingColoring ? "SAT" : "UNSAT")}, " +
                              $"összes döntés={result.DecisionNodes:N0}, idő={result.Elapsed}");

            if (result.HasAvoidingColoring)
            {
                lastAvoidingColoring = result.Coloring
                    ?? throw new InvalidOperationException("A SAT-eredményhez hiányzik a színezési tanú.");
                continue;
            }

            var worstColoring = lastAvoidingColoring ?? Enumerable.Repeat(true, edgeCount).ToArray();
            var longerWitness = FindNonNestedPathOfLength(m, worstColoring, k + 1);
            if (longerWitness is not null)
                return Fail($"A korábbi SAT-színezésben {k + 1} hosszú non-nested út is található; az eredmény nem konzisztens.");

            var witness = FindNonNestedPathOfLength(m, worstColoring, k);
            if (witness is null)
                return Fail($"A közvetlen ellenőrző nem talált {k} hosszú utat az UNSAT által implikált színezésben.");

            totalSw.Stop();
            Console.WriteLine();
            Console.WriteLine($"Legrosszabb eset: L_min={k} él ({k + 1} csúcs)");
            Console.WriteLine("Érvelés: a k-utas elkerülő formula UNSAT, a k+1-utas elkerülő formula SAT.");
            PrintWitness(witness);
            Console.WriteLine($"Teljes idő: {totalSw.Elapsed}");
            Console.WriteLine("SAT MIN-MAX VERIFICATION: PASS");
            EmitColoring(m, worstColoring, showColoring, outputFile);
            return 0;
        }

        return Fail("Váratlan: még a k=1 út elkerülő formulája is SAT lett.");
    }

    /// <summary>
    /// Preserves the original explicit path-CNF route for K_8..K_13. This
    /// avoids changing behavior for sizes which were already supported before
    /// the lazy large-instance extension.
    /// </summary>
    private static PathAvoidanceResult SolveAvoidingPathsExplicit(int m, int targetPathLength)
    {
        if (m < 2 || targetPathLength < 1 || targetPathLength >= m)
            throw new ArgumentOutOfRangeException(nameof(targetPathLength));

        var sw = Stopwatch.StartNew();
        var instance = BuildExplicitPathAvoidanceInstance(m, targetPathLength);
        var solver = new DpllSolver(instance);
        var fixedOk = solver.FixVariable(0, true); // color-swap symmetry
        var hasAvoidingColoring = fixedOk && solver.Solve();
        var coloring = hasAvoidingColoring ? solver.GetAssignment() : null;
        sw.Stop();

        return new PathAvoidanceResult(
            HasAvoidingColoring: hasAvoidingColoring,
            Coloring: coloring,
            LearnedPathCount: instance.MatchingsGenerated,
            SolverIterations: 1,
            DecisionNodes: solver.DecisionNodes,
            Elapsed: sw.Elapsed);
    }

    private static CnfInstance BuildExplicitPathAvoidanceInstance(int m, int targetPathLength)
    {
        var paths = NonNestedPathGenerator.Generate(m, targetPathLength);
        var forbiddenSets = new int[paths.Count][];
        for (var i = 0; i < paths.Count; i++)
            forbiddenSets[i] = paths[i].EdgeIds;

        return CnfInstance.BuildFromForbiddenEdgeSets(m, targetPathLength, forbiddenSets);
    }

    /// <summary>
    /// Lazily solves the coloring problem that avoids every monochromatic
    /// non-nested path with exactly targetPathLength edges. Each SAT candidate
    /// is checked directly; a violating path contributes a valid pair of CNF
    /// clauses. UNSAT of this subset of valid constraints proves UNSAT of the
    /// full path family, without ever enumerating that family.
    /// </summary>
    internal static PathAvoidanceResult SolveAvoidingPathsLazy(int m, int targetPathLength, bool showProgress = false)
    {
        if (m < 2 || targetPathLength < 1 || targetPathLength >= m)
            throw new ArgumentOutOfRangeException(nameof(targetPathLength));

        var edgeCount = checked(m * (m - 1) / 2);
        var solver = new IncrementalDpllSolver(edgeCount);
        long learnedPathCount = 0;
        long decisions = 0;
        long iterations = 0;
        var totalSw = Stopwatch.StartNew();

        while (true)
        {
            iterations++;
            solver.ResetForNextSolve();
            var fixedOk = solver.FixVariable(0, true); // color-swap symmetry
            var hasCandidate = fixedOk && solver.Solve();
            decisions += solver.DecisionNodes;

            if (!hasCandidate)
            {
                totalSw.Stop();
                return new PathAvoidanceResult(
                    HasAvoidingColoring: false,
                    Coloring: null,
                    LearnedPathCount: learnedPathCount,
                    SolverIterations: iterations,
                    DecisionNodes: decisions,
                    Elapsed: totalSw.Elapsed);
            }

            var coloring = solver.GetAssignment();
            const int cutBatchSize = 128;
            var violatingPaths = FindNonNestedPathsOfLength(m, coloring, targetPathLength, cutBatchSize);
            if (violatingPaths.Count == 0)
            {
                totalSw.Stop();
                return new PathAvoidanceResult(
                    HasAvoidingColoring: true,
                    Coloring: coloring,
                    LearnedPathCount: learnedPathCount,
                    SolverIterations: iterations,
                    DecisionNodes: decisions,
                    Elapsed: totalSw.Elapsed);
            }

            solver.ResetForNextSolve();
            // The candidate satisfies every clause already learned. Therefore
            // none of its monochromatic violating paths can have been learned
            // in an earlier round. The path finder removes reverse duplicates
            // inside this batch, so no ever-growing global string set is needed.
            var newlyLearned = violatingPaths.Count;
            foreach (var edgeIds in violatingPaths)
            {
                solver.AddForbiddenPath(edgeIds);
                learnedPathCount++;
            }

            if (showProgress && (iterations == 1 || iterations % 25 == 0))
            {
                Console.WriteLine($"      lazy progress: iteráció={iterations:N0}, új utak={newlyLearned:N0}, tanult utak={learnedPathCount:N0}, döntések={decisions:N0}");
            }
        }
    }

    /// <summary>
    /// Collects at most maxPaths distinct monochromatic non-nested paths from a
    /// candidate coloring. Bounded batches keep the lazy SAT loop from learning
    /// only one cut per full solver restart.
    /// </summary>
    private static List<int[]> FindNonNestedPathsOfLength(int m, bool[] red, int targetLength, int maxPaths)
    {
        var result = new List<int[]>(maxPaths);
        if (targetLength < 1 || targetLength >= m || maxPaths < 1)
            return result;

        var visited = new bool[m];
        var pathVertices = new int[targetLength + 1];
        var pathEdges = new int[targetLength];
        var localKeys = new HashSet<string>(StringComparer.Ordinal);
        var wantRed = false;

        for (var color = 0; color < 2 && result.Count < maxPaths; color++)
        {
            wantRed = color == 1;
            for (var start = 0; start < m && result.Count < maxPaths; start++)
            {
                Array.Clear(visited);
                visited[start] = true;
                pathVertices[0] = start;
                Dfs(0, start);
            }
        }
        return result;

        void Dfs(int depth, int last)
        {
            if (result.Count >= maxPaths)
                return;
            if (depth == targetLength)
            {
                // A path and its reverse are identical; keep one orientation.
                if (pathVertices[0] >= pathVertices[targetLength])
                    return;
                var canonicalEdges = (int[])pathEdges.Clone();
                Array.Sort(canonicalEdges);
                var key = string.Join(",", canonicalEdges);
                if (localKeys.Add(key))
                    result.Add(canonicalEdges);
                return;
            }

            for (var next = 0; next < m && result.Count < maxPaths; next++)
            {
                if (visited[next]) continue;
                var edgeId = NonNestedPathGenerator.EdgeId(m, last, next);
                if (red[edgeId] != wantRed) continue;

                var newEdge = (U: Math.Min(last, next), V: Math.Max(last, next));
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

                pathEdges[depth] = edgeId;
                pathVertices[depth + 1] = next;
                visited[next] = true;
                Dfs(depth + 1, next);
                visited[next] = false;
            }
        }
    }

    /// <summary>Finds one monochromatic non-nested path of exactly the requested length, stopping as soon as one is found.</summary>
    internal static MonochromaticPathWitness? FindNonNestedPathOfLength(int m, bool[] red, int targetLength)
    {
        if (red.Length != m * (m - 1) / 2)
            throw new ArgumentException("Hibás élszínezés-hossz.", nameof(red));
        if (targetLength < 1 || targetLength >= m)
            return null;

        var visited = new bool[m];
        var pathVertices = new int[targetLength + 1];
        var wantRed = false;

        for (var color = 0; color < 2; color++)
        {
            wantRed = color == 1;
            for (var start = 0; start < m; start++)
            {
                Array.Clear(visited);
                visited[start] = true;
                pathVertices[0] = start;
                if (Dfs(0, start))
                    return new MonochromaticPathWitness(wantRed, (int[])pathVertices.Clone());
            }
        }
        return null;

        bool Dfs(int depth, int last)
        {
            if (depth == targetLength)
                return true;

            for (var next = 0; next < m; next++)
            {
                if (visited[next]) continue;
                var edgeId = NonNestedPathGenerator.EdgeId(m, last, next);
                if (red[edgeId] != wantRed) continue;

                var newEdge = (U: Math.Min(last, next), V: Math.Max(last, next));
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
                if (Dfs(depth + 1, next))
                    return true;
                visited[next] = false;
            }
            return false;
        }
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
