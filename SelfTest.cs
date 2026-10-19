namespace OrderedMatching;

internal static class SelfTest
{
    public static int Run(bool runSat, bool requireNauty)
    {
        var passed = 0;
        var failed = 0;

        void Check(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine($"[PASS] {name}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] {name}: {ex.Message}");
                failed++;
            }
        }

        Check("Nested predicate", TestNestedPredicate);
        Check("Matching counts n=1..5", TestMatchingCounts);
        Check("Known lower-bound coloring n=2, m=4", TestKnownLowerBoundN2);
        Check("Known lower-bound coloring n=3, m=7", TestKnownLowerBoundN3);
        Check("All-red coloring has a legal witness", TestAllRedHasWitness);
        Check("Independent matching generator counts", TestIndependentMatchingCounts);
        Check("Compact n=6 matching count", TestCompactN6Count);
        Check("Matching count formula for custom m", TestMatchingCountFormula);
        Check("Matching case inspector indexes", TestMatchingCaseIndexes);
        Check("Independent lower-bound coloring n=2, m=4", TestIndependentLowerBoundN2);
        Check("Independent lower-bound coloring n=3, m=7", TestIndependentLowerBoundN3);
        Check("Compact lower-bound coloring n=6, m=16", TestCompactLowerBoundN6M16);
        if (runSat)
        {
            Check("Independent solver agrees: n=2, m=5", () => TestIndependentSolver(2, 5));
            Check("Independent solver agrees: n=3, m=8", () => TestIndependentSolver(3, 8));
            Check("Compact independent solver SAT: n=6, m=16", TestCompactIndependentSolverN6M16);
        }

        if (runSat)
        {
            Check("SAT regression: n=2, m=4 is SAT", () => TestSatCounterexample(2, 4));
            Check("SAT regression: n=2, m=5 is UNSAT", () => TestSatUnSat(2, 5));
            Check("SAT regression: n=3, m=7 is SAT", () => TestSatCounterexample(3, 7));
            Check("SAT regression: n=3, m=8 is UNSAT", () => TestSatUnSat(3, 8));
        }

        var nautyAvailable = NautyCanonicalizer.IsAvailable();
        if (nautyAvailable)
        {
            Check("nauty: reversal-invariant canonical key", TestNautyReversal);
            Check("nauty: non-reversal coloring distinguished", TestNautyDistinguishes);
            Check("nauty: automorphism order", TestNautyAutomorphismOrder);
        }
        else
        {
            var message = "nauty natív könyvtár nincs elérhető; a nauty teszteket kihagyom.";
            Console.WriteLine($"[SKIP] {message}");
            if (!string.IsNullOrWhiteSpace(NautyCanonicalizer.LastLoadError))
            {
                Console.WriteLine("       Betöltési hiba:");
                Console.WriteLine(NautyCanonicalizer.LastLoadError);
            }
            if (requireNauty)
            {
                Console.WriteLine("       A --require-nauty miatt ez hibának számít.");
                failed++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Összesen: {passed} PASS, {failed} FAIL");
        return failed == 0 ? 0 : 1;
    }

    private static void TestNestedPredicate()
    {
        Expect(NestedRelations.IsNested((0, 3), (1, 2)), true);
        Expect(NestedRelations.IsNested((1, 2), (0, 3)), true);
        Expect(NestedRelations.IsNested((0, 2), (1, 3)), false);
        Expect(NestedRelations.IsNested((0, 1), (2, 3)), false);
        Expect(NestedRelations.IsNested((0, 4), (2, 3)), true);
    }

    private static void TestMatchingCounts()
    {
        var expected = new Dictionary<int, (int M, int All)>
        {
            [1] = (1, 1),
            [2] = (10, 15),
            [3] = (140, 420),
            [4] = (2310, 17325),
            [5] = (42042, 945945)
        };

        foreach (var (n, counts) in expected)
        {
            var m = 3 * n - 1;
            var nestedFree = CnfInstance.Build(m, n, true);
            var unrestricted = CnfInstance.Build(m, n, false);
            Expect(nestedFree.MatchingsGenerated, counts.M);
            Expect(unrestricted.MatchingsGenerated, counts.All);
        }
    }

    private static void TestIndependentMatchingCounts()
    {
        var expected = new Dictionary<int, int>
        {
            [1] = 1,
            [2] = 10,
            [3] = 140,
            [4] = 2310,
            [5] = 42042
        };

        foreach (var (n, count) in expected)
        {
            var model = IndependentMatchingModel.Build(3 * n - 1, n, true);
            Expect(model.Matchings.Count, count);
        }
    }

    private static void TestIndependentLowerBoundN2()
    {
        var model = IndependentMatchingModel.Build(4, 2, true);
        var red = new bool[model.EdgeCount];
        foreach (var (u, v) in model.Edges)
            red[model.GetEdgeId(u, v)] = u < 3 && v < 3;
        Expect(IndependentColoringVerifier.IsAvoiding(model, red), true);
    }

    private static void TestIndependentLowerBoundN3()
    {
        var model = IndependentMatchingModel.Build(7, 3, true);
        var red = new bool[model.EdgeCount];
        foreach (var (u, v) in model.Edges)
            red[model.GetEdgeId(u, v)] = u < 5 && v < 5;
        Expect(IndependentColoringVerifier.IsAvoiding(model, red), true);
    }

    private static void TestIndependentSolver(int n, int m)
    {
        var model = IndependentMatchingModel.Build(m, n, true);
        var solver = new IndependentSearchSolver(model);
        var result = solver.Solve(useColorSymmetry: true);
        var expectedSat = m < 3 * n - 1;
        Expect(result.IsSat, expectedSat);
        Expect(IndependentColoringVerifier.IsAvoiding(model, result.Assignment), expectedSat);
    }

    private static void TestAllRedHasWitness()
    {
        var instance = CnfInstance.Build(8, 3, true);
        var coloring = Enumerable.Repeat(true, instance.EdgeCount).ToArray();
        var witness = ColoringVerifier.FindWitness(instance, coloring);
        if (witness is null)
            throw new InvalidOperationException("Nem találtam piros witness-t.");
        ColoringVerifier.AssertWitnessIsLegal(instance, witness);
    }


    private static void TestCompactLowerBoundN6M16()
    {
        var primary = CompactMatchingModel.Build(16, 6);
        var independent = CompactIndependentMatchingModel.Build(16, 6);
        Expect(primary.MatchingCount, 240240);
        Expect(independent.MatchingCount, 240240);
        var coloring = CompactColoringVerifier.ClassicalLowerBoundColoring(16, 6);
        Expect(CompactColoringVerifier.IsAvoiding(primary, coloring), true);
        Expect(CompactColoringVerifier.IsAvoiding(independent, coloring), true);
    }

    private static void TestCompactIndependentSolverN6M16()
    {
        var model = CompactIndependentMatchingModel.Build(16, 6);
        var solver = new CompactIndependentSolver(model);
        var result = solver.Solve(fixFirstRed: true);
        Expect(result.IsSat, true);
        Expect(CompactColoringVerifier.IsAvoiding(model, result.Assignment), true);
    }

    private static void TestKnownLowerBoundN2()
    {
        var instance = CnfInstance.Build(4, 2, true);
        var coloring = BuildClassicalCounterexample(instance, cliqueSize: 3);
        Expect(ColoringVerifier.IsValidAvoidingColoring(instance, coloring), true);
    }

    private static void TestKnownLowerBoundN3()
    {
        var instance = CnfInstance.Build(7, 3, true);
        var coloring = BuildClassicalCounterexample(instance, cliqueSize: 5);
        Expect(ColoringVerifier.IsValidAvoidingColoring(instance, coloring), true);
    }

    private static bool[] BuildClassicalCounterexample(CnfInstance instance, int cliqueSize)
    {
        var red = new bool[instance.EdgeCount];
        for (var e = 0; e < instance.EdgeCount; e++)
        {
            var (u, v) = instance.GetEdge(e);
            red[e] = u < cliqueSize && v < cliqueSize;
        }
        return red;
    }

    private static void TestSatCounterexample(int n, int m)
    {
        var instance = CnfInstance.Build(m, n, true);
        var solver = new DpllSolver(instance);
        Expect(solver.Solve(), true);
        var coloring = solver.GetAssignment();
        Expect(ColoringVerifier.IsValidAvoidingColoring(instance, coloring), true);
        if (ColoringVerifier.FindWitness(instance, coloring) is not null)
            throw new InvalidOperationException("A SAT ellenpéldában mégis van tiltott monochromatikus witness.");
    }

    private static void TestSatUnSat(int n, int m)
    {
        var instance = CnfInstance.Build(m, n, true);
        var solver = new DpllSolver(instance);
        Expect(solver.FixVariable(0, true), true); // színcsere-szimmetria
        Expect(solver.Solve(), false);
    }

    private static void TestNautyReversal()
    {
        var instance = CnfInstance.Build(8, 3, true);
        var a = new bool[instance.EdgeCount];
        a[instance.GetEdgeId(0, 1)] = true;
        a[instance.GetEdgeId(1, 4)] = true;

        var b = ReverseColoring(instance, a);
        var ka = NautyCanonicalizer.Canonicalize(instance, a);
        var kb = NautyCanonicalizer.Canonicalize(instance, b);
        Expect(ka.Key, kb.Key);
    }

    private static void TestNautyDistinguishes()
    {
        var instance = CnfInstance.Build(8, 3, true);
        var a = new bool[instance.EdgeCount];
        var b = new bool[instance.EdgeCount];
        a[instance.GetEdgeId(0, 1)] = true;
        b[instance.GetEdgeId(0, 2)] = true;

        var ka = NautyCanonicalizer.Canonicalize(instance, a);
        var kb = NautyCanonicalizer.Canonicalize(instance, b);
        if (ka.Key == kb.Key)
            throw new InvalidOperationException("A nauty tévesen azonosította a nem megfordítással ekvivalens színezéseket.");
    }

    private static void TestNautyAutomorphismOrder()
    {
        var instance = CnfInstance.Build(8, 3, true);
        var allBlue = new bool[instance.EdgeCount];
        var asym = new bool[instance.EdgeCount];
        asym[instance.GetEdgeId(0, 1)] = true;

        var symmetric = NautyCanonicalizer.Canonicalize(instance, allBlue);
        var asymmetric = NautyCanonicalizer.Canonicalize(instance, asym);
        Expect(symmetric.AutomorphismOrder, 2);
        Expect(asymmetric.AutomorphismOrder, 1);
    }

    private static bool[] ReverseColoring(CnfInstance instance, bool[] coloring)
    {
        var reversed = new bool[coloring.Length];
        var m = instance.VertexCount;
        for (var u = 0; u < m; u++)
        for (var v = u + 1; v < m; v++)
        {
            var oldId = instance.GetEdgeId(u, v);
            var newId = instance.GetEdgeId(m - 1 - v, m - 1 - u);
            reversed[newId] = coloring[oldId];
        }
        return reversed;
    }

    private static void TestCompactN6Count()
    {
        var model = CompactMatchingModel.Build(17, 6);
        Expect(model.MatchingCount, 816816);
    }

    private static void TestMatchingCountFormula()
    {
        Expect(MatchingCountMath.CountNonNested(17, 6), new System.Numerics.BigInteger(816816));
        Expect(MatchingCountMath.CountNonNested(16, 6), new System.Numerics.BigInteger(240240));
        Expect(MatchingCountMath.CountNonNested(7, 3), new System.Numerics.BigInteger(35));
        Expect(MatchingCountMath.CountNonNested(5, 3), System.Numerics.BigInteger.Zero);
    }

    private static void TestMatchingCaseIndexes()
    {
        var model = CompactMatchingModel.Build(8, 3);
        for (var caseIndex = 0; caseIndex < model.MatchingCount; caseIndex++)
        {
            var actual = MatchingCaseInspector.FindMatchingAtIndex(8, 3, caseIndex, forbidNested: true);
            if (actual is null)
                throw new InvalidOperationException($"A case inspector nem talált matchinget a {caseIndex + 1}. indexen.");

            var actualIds = actual.Select(e => model.GetEdgeId(e.U, e.V)).OrderBy(x => x).ToArray();
            var expectedIds = model.Matching(caseIndex).ToArray().OrderBy(x => x).ToArray();
            if (!actualIds.SequenceEqual(expectedIds))
                throw new InvalidOperationException($"A case index sorrend eltér a generátortól a {caseIndex + 1}. indexen.");

            for (var i = 0; i < actual.Length; i++)
            for (var j = i + 1; j < actual.Length; j++)
            {
                Expect(NestedRelations.IsNested(actual[i], actual[j]), false);
                if (actual[i].U == actual[j].U || actual[i].U == actual[j].V || actual[i].V == actual[j].U || actual[i].V == actual[j].V)
                    throw new InvalidOperationException("A case inspector matchingje két közös csúcsú élt tartalmaz.");
            }
        }
    }

    private static void Expect<T>(T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            throw new InvalidOperationException($"Várt: {expected}; kapott: {actual}.");
    }
}
