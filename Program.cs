using System.Diagnostics;

namespace OrderedMatching;

internal static class Program
{
    private static void Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options.Help) { Options.PrintHelp(); return; }

        if (options.SelfTest)
        {
            Environment.ExitCode = SelfTest.Run(options.RunSatTests, options.RequireNauty);
            return;
        }

        if (options.VerifyAll)
        {
            Environment.ExitCode = VerificationEdition.RunAll();
            return;
        }

        if (options.N < 1)
        {
            Console.Error.WriteLine("Az n pozitív egész kell legyen.");
            Environment.ExitCode = 2;
            return;
        }

        if (options.Verify)
        {
            Environment.ExitCode = options.N == 6
                ? ResearchEdition.RunN6(runIndependent: true)
                : VerificationEdition.RunOne(options.N, options.UseNauty);
            return;
        }

        if (options.N == 6 && options.ForbidNested)
        {
            Environment.ExitCode = ResearchEdition.RunN6(runIndependent: true);
            return;
        }

        RunSingle(options);
    }

    private static void RunSingle(Options options)
    {
        var m = checked(3 * options.N - 1);
        if (m > 28 && !options.Force)
        {
            Console.Error.WriteLine($"m = {m}. Ez már nagyon nagy keresési tér. Használd a --force kapcsolót, ha szándékosan indítod.");
            Environment.ExitCode = 2;
            return;
        }

        Console.WriteLine("Ordered non-nested matching search");
        Console.WriteLine($"n = {options.N},  m = 3n-1 = {m}");
        Console.WriteLine($"Ölelő élek tiltva: {options.ForbidNested}");
        Console.WriteLine("Színek: piros / kék");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        var instance = CnfInstance.Build(m, options.N, options.ForbidNested);
        Console.WriteLine($"Élek száma:                 {instance.EdgeCount}");
        Console.WriteLine($"Nem-ölelő M_n-ek száma:     {instance.MatchingsGenerated}");
        Console.WriteLine($"SAT-klauzulák száma:        {instance.ClauseCount}");
        Console.WriteLine();

        var solver = new DpllSolver(instance);
        if (!solver.FixVariable(0, true))
        {
            Console.WriteLine("UNSAT (már a szimmetria-rögzítés is ellentmondásos).");
            return;
        }

        var sat = solver.Solve();
        sw.Stop();

        Console.WriteLine($"Eredmény: {(sat ? "SAT – találtam ellenpélda-színezést" : "UNSAT – nincs ilyen színezés")}");
        Console.WriteLine($"Döntési csomópontok:        {solver.DecisionNodes}");
        Console.WriteLine($"Idő:                         {sw.Elapsed}");

        if (!sat) return;

        var coloring = solver.GetAssignment();
        var verifierWitness = ColoringVerifier.FindWitness(instance, coloring);
        if (verifierWitness is not null)
            throw new InvalidOperationException("Belső hiba: a SAT megoldás verifier szerint tiltott witness-t tartalmaz.");

        if (options.UseNauty)
        {
            if (!NautyCanonicalizer.IsAvailable())
            {
                Console.WriteLine("nauty: nincs betöltve.");
                return;
            }
            var canonical = NautyCanonicalizer.Canonicalize(instance, coloring);
            Console.WriteLine($"nauty automorfizmusok száma: {canonical.AutomorphismOrder}");
            Console.WriteLine($"nauty kanonikus kulcs:       {canonical.Key}");
        }
    }
}

internal sealed class Options
{
    public int N { get; private set; } = 3;
    public bool ForbidNested { get; private set; } = true;
    public bool Force { get; private set; }
    public bool Help { get; private set; }
    public bool SelfTest { get; private set; }
    public bool RunSatTests { get; private set; } = true;
    public bool RequireNauty { get; private set; }
    public bool UseNauty { get; private set; }
    public bool Verify { get; private set; }
    public bool VerifyAll { get; private set; }
    public string? OutputFile { get; private set; }

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-n":
                case "--n":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out var n))
                        throw new ArgumentException("A --n után egész szám kell.");
                    o.N = n;
                    break;
                case "--allow-nested": o.ForbidNested = false; break;
                case "--force": o.Force = true; break;
                case "--self-test": o.SelfTest = true; break;
                case "--no-sat-tests": o.RunSatTests = false; break;
                case "--require-nauty": o.RequireNauty = true; break;
                case "--nauty": o.UseNauty = true; break;
                case "--verify": o.Verify = true; break;
                case "--verify-all": o.VerifyAll = true; break;
                case "-o":
                case "--output":
                    if (i + 1 >= args.Length) throw new ArgumentException("A --output után fájlnév kell.");
                    o.OutputFile = args[++i];
                    break;
                case "-h":
                case "--help": o.Help = true; break;
                default: throw new ArgumentException($"Ismeretlen kapcsoló: {args[i]}");
            }
        }
        return o;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Használat:");
        Console.WriteLine("  dotnet run -- -n 3");
        Console.WriteLine("  dotnet run -- -n 3 --nauty");
        Console.WriteLine("  dotnet run -- --verify -n 4 --nauty");
        Console.WriteLine("  dotnet run -- --verify-all");
        Console.WriteLine("  dotnet run -- --self-test --require-nauty");
        Console.WriteLine();
        Console.WriteLine("Kapcsolók:");
        Console.WriteLine("  --n, -n N           n értéke (alapértelmezés: 3)");
        Console.WriteLine("  --allow-nested      ölelő élek engedélyezése");
        Console.WriteLine("  --nauty             SAT megoldás kanonikusítása nauty-val");
        Console.WriteLine("  --verify            DPLL eredmény független solverrel is ellenőrizve");
        Console.WriteLine("  --verify-all        n=2..5 teljes független keresztellenőrzés");
        Console.WriteLine("  -n 6                Research Edition: tömör két-solveres n=6 ellenőrzés");
        Console.WriteLine("  --self-test         visszaellenőrző tesztek futtatása");
        Console.WriteLine("  --no-sat-tests      self-test alatt a SAT regressziókat kihagyja");
        Console.WriteLine("  --require-nauty     self-testben kötelezővé teszi a natív nauty-t");
        Console.WriteLine("  --force             nagyobb m érték engedélyezése");
        Console.WriteLine("  --help, -h          segítség");
    }
}
