using System.Diagnostics;
using System.Numerics;

namespace OrderedMatching;

internal static class ResearchEdition
{
    // Backwards-compatible entry point used by the earlier n=6 edition.
    public static int RunN6(bool runIndependent = true) => Run(6, 17, runIndependent, force: false);

    public static int Run(
        int n,
        int m,
        bool runIndependent = true,
        bool force = false,
        bool showColoring = false,
        string? outputFile = null)
    {
        if (n < 2 || n > 7)
        {
            Console.Error.WriteLine("A tömör, két-solveres Verification/Research mód jelenleg 2 <= n <= 7 tartományt támogat.");
            return 2;
        }
        if (m < 0 || m > 28)
        {
            Console.Error.WriteLine("A Research mód jelenleg 0 <= m <= 28 csúcsot támogat.");
            return 2;
        }

        var theoreticalCount = MatchingCountMath.CountNonNested(m, n);
        if (theoreticalCount.IsZero)
        {
            Console.WriteLine($"=== Research Edition: n={n}, m={m} ===");
            Console.WriteLine($"Mivel m={m} < 2n={2 * n}, ilyen méretű matching nem létezhet.");
            Console.WriteLine("Eredmény: SAT – ellenpélda-színezés triviálisan létezik, mert nincs vizsgálandó M_n.");
            Console.WriteLine("VERIFICATION: PASS (triviális eset)");
            if (showColoring || outputFile is not null)
            {
                var allBlue = new bool[m * (m - 1) / 2];
                var coloringText = FormatColoring(m, n, allBlue);
                if (showColoring) Console.WriteLine(coloringText);
                if (outputFile is not null)
                {
                    File.WriteAllText(outputFile, coloringText);
                    Console.WriteLine($"A teljes élszínezést ide mentettem: {Path.GetFullPath(outputFile)}");
                }
            }
            return 0;
        }

        if (theoreticalCount > int.MaxValue || theoreticalCount * n > int.MaxValue)
        {
            Console.Error.WriteLine($"A matchinghalmaz túl nagy a jelenlegi tömbalapú solverhez: {theoreticalCount:N0} matching.");
            Console.Error.WriteLine("Ez a verzió int-indexelt lapos tömböket használ, ezért a futás biztonságosan leáll.");
            return 2;
        }

        const int safeMatchingLimit = 1_000_000;
        if (theoreticalCount > safeMatchingLimit && !force)
        {
            Console.Error.WriteLine($"A becsült matching-szám {theoreticalCount:N0}, ami meghaladja a biztonságos {safeMatchingLimit:N0}-es határt.");
            Console.Error.WriteLine("Ha ezt tudatosan szeretnéd megpróbálni, add meg a --force kapcsolót.");
            return 2;
        }

        Console.WriteLine($"=== Research Edition: n={n}, m={m} ===");
        Console.WriteLine($"Cél: van-e monochromatikus, nem-nested M_{n}?");
        Console.WriteLine($"Elméleti nem-nested matching-szám: {theoreticalCount:N0}");
        Console.WriteLine();

        var totalSw = Stopwatch.StartNew();
        Console.WriteLine("[1/5] Elsődleges nem-ölelő matching-generálás...");
        var primary = CompactMatchingModel.Build(m, n);
        Console.WriteLine($"      M_{n}-ek: {primary.MatchingCount:N0}");
        if (new BigInteger(primary.MatchingCount) != theoreticalCount)
            return Fail("Az első matching-generátor darabszáma nem egyezik a képlettel.");

        Console.WriteLine("[2/5] Független generátor + exact packed-key ellenőrzés...");
        var independent = CompactIndependentMatchingModel.Build(m, n);
        Console.WriteLine($"      független M_{n}-ek: {independent.MatchingCount:N0}");
        if (independent.MatchingCount != primary.MatchingCount)
            return Fail("A két matching-generátor darabszáma eltér.");

        var aKeys = primary.BuildPackedKeys();
        var bKeys = independent.BuildPackedKeys();
        Array.Sort(aKeys);
        Array.Sort(bKeys);
        if (!aKeys.AsSpan().SequenceEqual(bKeys))
            return Fail("Az exact packed-key matchinghalmazok eltérnek.");
        Console.WriteLine("      Exact matching set: OK");

        // The classical boundary case m=3n-2 has an explicit avoiding coloring:
        // a red K_(2n-1), with every other edge blue. Check it against BOTH
        // matching lists before invoking either solver. This catches precisely
        // the kind of false UNSAT that occurred in the independent solver.
        if (n >= 2 && m == 3 * n - 2)
        {
            var knownWitness = CompactColoringVerifier.ClassicalLowerBoundColoring(m, n);
            if (!CompactColoringVerifier.IsAvoiding(primary, knownWitness) ||
                !CompactColoringVerifier.IsAvoiding(independent, knownWitness))
                return Fail("A klasszikus m=3n-2 alsókorlát-színezés nem ment át a matching-ellenőrzésen.");
            Console.WriteLine("      Klasszikus m=3n-2 SAT-tanú: PASS mindkét matchinghalmazon");
        }

        aKeys = Array.Empty<ulong>();
        bKeys = Array.Empty<ulong>();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Console.WriteLine("[3/5] A solver: lapos két-watchos DPLL...");
        var satSw = Stopwatch.StartNew();
        var satSolver = new CompactCnfSolver(primary, showProgress: true);
        Console.WriteLine("      SAT-struktúra felépítve; keresés indul.");
        var fixedOk = satSolver.FixVariable(0, true); // színcsere-szimmetria
        var satA = fixedOk && satSolver.Solve();
        // Keep a SAT witness even if the user did not request printed output:
        // it must be checked against both independently generated matching sets.
        var satAssignment = satA ? satSolver.GetAssignment() : null;
        satSw.Stop();
        Console.WriteLine($"      eredmény: {(satA ? "SAT" : "UNSAT")}");
        if (satAssignment is not null)
        {
            var validPrimary = CompactColoringVerifier.IsAvoiding(primary, satAssignment);
            var validIndependent = CompactColoringVerifier.IsAvoiding(independent, satAssignment);
            Console.WriteLine($"      A SAT-tanú ellenőrzése: {(validPrimary && validIndependent ? "PASS" : "FAIL")}");
            if (!validPrimary || !validIndependent)
                return Fail("Az A solver SAT-kiosztása nem éli túl a közvetlen matching-ellenőrzést.");
        }
        Console.WriteLine($"      decisions: {satSolver.DecisionNodes:N0}");
        Console.WriteLine($"      propagations: {satSolver.Propagations:N0}");
        Console.WriteLine($"      idő: {satSw.Elapsed}");

        // A nagy watcher-struktúrákat engedjük el a B solver előtt.
        satSolver = null!;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (!runIndependent)
        {
            totalSw.Stop();
            Console.WriteLine($"Egyetlen solver teljes ideje: {totalSw.Elapsed}");
            return 0;
        }

        Console.WriteLine("[4/5] B solver: független közvetlen hipergráf-keresés...");
        var directSolver = new CompactIndependentSolver(independent);
        var resultB = directSolver.Solve(fixFirstRed: true);
        Console.WriteLine($"      eredmény: {(resultB.IsSat ? "SAT" : "UNSAT")}");
        Console.WriteLine($"      decisions: {resultB.DecisionNodes:N0}");
        Console.WriteLine($"      propagations: {resultB.Propagations:N0}");
        Console.WriteLine($"      idő: {resultB.Elapsed}");
        if (resultB.IsSat)
        {
            var validPrimary = CompactColoringVerifier.IsAvoiding(primary, resultB.Assignment);
            var validIndependent = CompactColoringVerifier.IsAvoiding(independent, resultB.Assignment);
            Console.WriteLine($"      B SAT-tanú ellenőrzése: {(validPrimary && validIndependent ? "PASS" : "FAIL")}");
            if (!validPrimary || !validIndependent)
                return Fail("A B solver SAT-kiosztása nem éli túl a közvetlen matching-ellenőrzést.");
        }

        Console.WriteLine("[5/5] Keresők egyezésének ellenőrzése...");
        if (satA != resultB.IsSat)
        {
            if (satAssignment is not null)
            {
                Console.WriteLine("      Diagnosztika: az A solver SAT-tanúja a matching-generátorok mindkét listáján érvényes.");
                Console.WriteLine("      A B solver inkonzisztens eredményt adott; ezt nem szabad VERIFIED eredményként kezelni.");
                if (showColoring || outputFile is not null)
                {
                    var coloringText = FormatColoring(m, n, satAssignment);
                    if (showColoring) Console.WriteLine(coloringText);
                    if (outputFile is not null)
                    {
                        File.WriteAllText(outputFile, coloringText);
                        Console.WriteLine($"A SAT-tanú ide mentve: {Path.GetFullPath(outputFile)}");
                    }
                }
            }
            return Fail("A két független solver eltérő eredményt adott.");
        }

        totalSw.Stop();
        Console.WriteLine();
        Console.WriteLine($"VÉGEREDMÉNY: {(satA ? "SAT – ellenpélda létezik" : "UNSAT – nincs ilyen színezés")}");
        Console.WriteLine($"Teljes idő: {totalSw.Elapsed}");
        Console.WriteLine("VERIFICATION: PASS");

        if (satA && satAssignment is not null)
        {
            var coloringText = FormatColoring(m, n, satAssignment);
            if (showColoring)
            {
                Console.WriteLine();
                Console.WriteLine("Talált teljes K_m élszínezés:");
                Console.WriteLine(coloringText);
            }
            if (outputFile is not null)
            {
                File.WriteAllText(outputFile, coloringText);
                Console.WriteLine($"A teljes élszínezést ide mentettem: {Path.GetFullPath(outputFile)}");
            }
        }
        return 0;
    }

    private static string FormatColoring(int m, int n, bool[] red)
    {
        var expectedEdges = m * (m - 1) / 2;
        if (red.Length != expectedEdges)
            throw new InvalidOperationException("Hibás hosszúságú színezés a Research Edition formatterének.");
        var lines = new List<string> { $"n={n}", $"m={m}", "", "PIROS:" };
        var edgeId = 0;
        var blueEdges = new List<string>();
        for (var u = 0; u < m; u++)
        for (var v = u + 1; v < m; v++)
        {
            var edge = $"({u + 1},{v + 1})";
            if (red[edgeId]) lines.Add(edge);
            else blueEdges.Add(edge);
            edgeId++;
        }
        lines.Add("");
        lines.Add("KÉK:");
        lines.AddRange(blueEdges);
        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }

    private static int Fail(string message)
    {
        Console.WriteLine($"      FAIL: {message}");
        return 1;
    }
}
