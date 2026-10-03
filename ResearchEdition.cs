using System.Diagnostics;

namespace OrderedMatching;

internal static class ResearchEdition
{
    public static int RunN6(bool runIndependent = true)
    {
        const int n = 6;
        const int m = 17;
        Console.WriteLine("=== Research Edition: n=6 ===");
        Console.WriteLine("Keresés: m = 3n-1 = 17");
        Console.WriteLine("Cél: van-e monochromatikus, nem-nested M6?");
        Console.WriteLine();

        var totalSw = Stopwatch.StartNew();
        Console.WriteLine("[1/5] Elsődleges nem-ölelő M6 generálás...");
        var primary = CompactMatchingModel.Build(m, n);
        Console.WriteLine($"      M6-ek: {primary.MatchingCount:N0}");
        if (primary.MatchingCount != 816_816)
        {
            Console.WriteLine("      HIBA: várt matching-szám = 816816.");
            return 1;
        }

        Console.WriteLine("[2/5] Független generátor + exact packed-key ellenőrzés...");
        var independent = CompactIndependentMatchingModel.Build(m, n);
        Console.WriteLine($"      független M6-ek: {independent.MatchingCount:N0}");
        if (independent.MatchingCount != primary.MatchingCount)
            return Fail("A két matching-generátor darabszáma eltér.");

        var aKeys = primary.BuildPackedKeys();
        var bKeys = independent.BuildPackedKeys();
        Array.Sort(aKeys);
        Array.Sort(bKeys);
        if (!aKeys.AsSpan().SequenceEqual(bKeys))
            return Fail("Az exact packed-key matchinghalmazok eltérnek.");
        Console.WriteLine("      Exact matching set: OK");
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
        satSw.Stop();
        Console.WriteLine($"      eredmény: {(satA ? "SAT" : "UNSAT")}");
        Console.WriteLine($"      decisions: {satSolver.DecisionNodes:N0}");
        Console.WriteLine($"      propagations: {satSolver.Propagations:N0}");
        Console.WriteLine($"      idő: {satSw.Elapsed}");

        // A nagy watcher-struktúrákat engedjük el a B solver előtt.
        satSolver = null!;
        aKeys = Array.Empty<ulong>();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (!runIndependent)
        {
            totalSw.Stop();
            return 0;
        }

        Console.WriteLine("[4/5] B solver: független közvetlen hipergráf-keresés...");
        var directSolver = new CompactIndependentSolver(independent);
        var resultB = directSolver.Solve(fixFirstRed: true);
        Console.WriteLine($"      eredmény: {(resultB.IsSat ? "SAT" : "UNSAT")}");
        Console.WriteLine($"      decisions: {resultB.DecisionNodes:N0}");
        Console.WriteLine($"      propagations: {resultB.Propagations:N0}");
        Console.WriteLine($"      idő: {resultB.Elapsed}");

        Console.WriteLine("[5/5] Keresők egyezésének ellenőrzése...");
        if (satA != resultB.IsSat)
            return Fail("A két független solver eltérő eredményt adott.");

        totalSw.Stop();
        Console.WriteLine();
        Console.WriteLine($"VÉGEREDMÉNY: {(satA ? "SAT – ellenpélda létezik" : "UNSAT – nincs ilyen színezés")}");
        Console.WriteLine($"Teljes idő: {totalSw.Elapsed}");
        Console.WriteLine("VERIFICATION: PASS");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.WriteLine($"      FAIL: {message}");
        return 1;
    }
}
