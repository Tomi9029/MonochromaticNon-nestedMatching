using System.Diagnostics;

namespace OrderedMatching;

internal static class VerificationEdition
{
    public static int RunOne(int n, bool useNauty, int? vertexCount = null, bool force = false)
    {
        if (n < 1 || n > 5)
        {
            Console.Error.WriteLine("A listás --verify jelenleg 1 <= n <= 5 tartományra van korlátozva; n>=6 esetén a tömör Research mód használható.");
            return 2;
        }

        var m = vertexCount ?? checked(3 * n - 1);
        if (m < 0 || m > 28)
        {
            Console.Error.WriteLine("A --verify mód 0 <= m <= 28 tartományt támogat.");
            return 2;
        }
        var estimatedMatchings = MatchingCountMath.CountNonNested(m, n);
        if (estimatedMatchings > 300_000 && !force)
        {
            Console.Error.WriteLine($"A becsült matching-szám {estimatedMatchings:N0}; ez túl nagy a listás Verification módhoz.");
            Console.Error.WriteLine("Használd a --force kapcsolót, vagy válassz kisebb m-et.");
            return 2;
        }
        if (2 * n > m)
        {
            Console.WriteLine("=== Verification Edition ===");
            Console.WriteLine($"n={n}, m={m}");
            Console.WriteLine("SAT: m < 2n, tehát n élű matching nem létezhet.");
            Console.WriteLine("VERIFICATION: PASS (triviális eset)");
            return 0;
        }

        Console.WriteLine("=== Verification Edition ===");
        Console.WriteLine($"n={n}, m={m}");
        Console.WriteLine("A: eredeti CNF + watcholt DPLL");
        Console.WriteLine("B: független matching-modell + közvetlen állapottér-kereső");
        Console.WriteLine();

        var aModel = CnfInstance.Build(m, n, true);
        var bModel = IndependentMatchingModel.Build(m, n, true);
        Console.WriteLine($"A matchings: {aModel.MatchingsGenerated}");
        Console.WriteLine($"B matchings: {bModel.Matchings.Count}");

        if (aModel.MatchingsGenerated != bModel.Matchings.Count)
        {
            Console.WriteLine("FAIL: a két független matching-generátor eltérő darabszámot ad.");
            return 1;
        }

        var exactMatchingsA = ExtractCnfMatchingSet(aModel);
        var exactMatchingsB = bModel.Matchings
            .Select(matching => string.Join(',', matching.EdgeIds.OrderBy(x => x)))
            .ToHashSet(StringComparer.Ordinal);
        if (!exactMatchingsA.SetEquals(exactMatchingsB))
        {
            Console.WriteLine("FAIL: a két matching-generátor darabszáma azonos, de a konkrét matchinghalmaz eltér.");
            var missing = exactMatchingsA.Except(exactMatchingsB).Take(3).ToArray();
            var extra = exactMatchingsB.Except(exactMatchingsA).Take(3).ToArray();
            Console.WriteLine($"  hiányzó B-ből: {string.Join(" | ", missing)}");
            Console.WriteLine($"  extra B-ben:   {string.Join(" | ", extra)}");
            return 1;
        }
        Console.WriteLine("Exact matching set: OK");

        var swA = Stopwatch.StartNew();
        var solverA = new DpllSolver(aModel);
        if (!solverA.FixVariable(0, true))
            return 1;
        var satA = solverA.Solve();
        swA.Stop();

        var swB = Stopwatch.StartNew();
        var solverB = new IndependentSearchSolver(bModel);
        var resultB = solverB.Solve(useColorSymmetry: true);
        swB.Stop();

        Console.WriteLine();
        Console.WriteLine($"A / DPLL:       {(satA ? "SAT" : "UNSAT")}   decisions={solverA.DecisionNodes}   time={swA.Elapsed}");
        Console.WriteLine($"B / independent:{(resultB.IsSat ? "SAT" : "UNSAT")}   decisions={resultB.DecisionNodes}   time={swB.Elapsed}");

        if (satA != resultB.IsSat)
        {
            Console.WriteLine("FAIL: a két független kereső eltérő eredményt adott.");
            return 1;
        }

        if (satA)
        {
            var assignmentA = solverA.GetAssignment();
            var assignmentB = resultB.Assignment;
            var verifyA = IndependentColoringVerifier.IsAvoiding(bModel, assignmentA);
            var verifyB = IndependentColoringVerifier.IsAvoiding(bModel, assignmentB);
            Console.WriteLine($"A modell B-verifieren:       {(verifyA ? "OK" : "FAIL")}");
            Console.WriteLine($"B modell B-verifieren:       {(verifyB ? "OK" : "FAIL")}");
            if (!verifyA || !verifyB) return 1;

            if (useNauty)
            {
                if (!NautyCanonicalizer.IsAvailable())
                {
                    Console.WriteLine("nauty: nincs elérhető natív könyvtár.");
                    return 1;
                }
                var ca = NautyCanonicalizer.Canonicalize(aModel, assignmentA);
                var cb = NautyCanonicalizer.Canonicalize(aModel, assignmentB);
                Console.WriteLine($"nauty A automorfizmus: {ca.AutomorphismOrder}");
                Console.WriteLine($"nauty B automorfizmus: {cb.AutomorphismOrder}");
                Console.WriteLine($"nauty kulcsok egyeznek: {ca.Key == cb.Key}");
                if (ca.Key != cb.Key) return 1;
            }
        }

        Console.WriteLine("VERIFICATION: PASS");
        return 0;
    }

    private static HashSet<string> ExtractCnfMatchingSet(CnfInstance instance)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < instance.MatchingsGenerated; i++)
        {
            var clause = instance.GetClause(2 * i);
            result.Add(string.Join(',', clause.ToArray().Select(x => x / 2).OrderBy(x => x)));
        }
        return result;
    }

    public static int RunAll()
    {
        Console.WriteLine("=== Verification Edition: n=2..5 ===");
        var expected = new Dictionary<int, bool>
        {
            [2] = false, // m=5, nincs ellenpélda
            [3] = false,
            [4] = false,
            [5] = false
        };

        foreach (var n in expected.Keys)
        {
            Console.WriteLine();
            var rc = RunOne(n, useNauty: false);
            if (rc != 0)
            {
                Console.WriteLine($"STOP: n={n} verification sikertelen.");
                return rc;
            }
        }

        Console.WriteLine();
        Console.WriteLine("ALL VERIFICATIONS PASSED");
        return 0;
    }
}
