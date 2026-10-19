using System.Diagnostics;
using System.Numerics;

namespace OrderedMatching;

internal static class Program
{
    private static void Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Options.PrintHelp();
            Environment.ExitCode = 2;
            return;
        }

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

        int m;
        try
        {
            m = options.M ?? checked(3 * options.N - 1);
        }
        catch (OverflowException)
        {
            Console.Error.WriteLine("Az n túl nagy.");
            Environment.ExitCode = 2;
            return;
        }

        if (m < 0)
        {
            Console.Error.WriteLine("Az m nem lehet negatív.");
            Environment.ExitCode = 2;
            return;
        }
        if (m > 28 && !options.Force)
        {
            Console.Error.WriteLine($"m = {m}. Ez már nagyon nagy keresési tér. Használd a --force kapcsolót, ha szándékosan indítod.");
            Environment.ExitCode = 2;
            return;
        }

        if (options.InspectMatchingIndex.HasValue)
        {
            Environment.ExitCode = MatchingCaseInspector.Run(
                m,
                options.N,
                options.InspectMatchingIndex.Value,
                options.ForbidNested,
                options.MaxColorings);
            return;
        }

        if (options.Verify)
        {
            if (!options.ForbidNested)
            {
                Console.Error.WriteLine("A --verify jelenleg a nem-nested (ölelő élt tiltó) modellhez használható; vedd le a --allow-nested kapcsolót.");
                Environment.ExitCode = 2;
                return;
            }

            Environment.ExitCode = options.N >= 6
                ? ResearchEdition.Run(options.N, m, runIndependent: true, force: options.Force,
                    showColoring: options.ShowColoring, outputFile: options.OutputFile)
                : VerificationEdition.RunOne(options.N, options.UseNauty, options.M, options.Force);
            return;
        }

        // The compact dual-solver route is needed for n=6, and is also used if
        // the user explicitly asks for an m value with n>=6.
        if (options.ForbidNested && (options.N == 6 || (options.M.HasValue && options.N >= 6)))
        {
            Environment.ExitCode = ResearchEdition.Run(options.N, m, runIndependent: true, force: options.Force,
                showColoring: options.ShowColoring, outputFile: options.OutputFile);
            return;
        }

        RunSingle(options, m);
    }

    private static void RunSingle(Options options, int m)
    {
        var n = options.N;
        if (m < 2 * n)
        {
            Console.WriteLine("Ordered non-nested matching search");
            Console.WriteLine($"n = {n}, m = {m}");
            Console.WriteLine($"Élek száma: {m * (m - 1) / 2}");
            Console.WriteLine($"Eredmény: SAT – m < 2n, ezért n élű párosítás nem is létezhet.");
            if (options.ShowColoring || options.OutputFile is not null)
            {
                var emptyInstance = CnfInstance.Build(m, n, options.ForbidNested);
                var allBlue = new bool[emptyInstance.EdgeCount];
                EmitColoring(emptyInstance, allBlue, options);
            }
            return;
        }

        var theoreticalMatchings = options.ForbidNested
            ? MatchingCountMath.CountNonNested(m, n)
            : MatchingCountMath.CountAll(m, n);
        const int safeMatchingLimit = 1_000_000;
        if (theoreticalMatchings > safeMatchingLimit && !options.Force)
        {
            Console.Error.WriteLine($"A becsült matching-szám {theoreticalMatchings:N0}, ami meghaladja a biztonságos {safeMatchingLimit:N0}-es határt.");
            Console.Error.WriteLine("Használd a --force kapcsolót, ha tudatosan szeretnéd elindítani.");
            Environment.ExitCode = 2;
            return;
        }

        Console.WriteLine("Ordered non-nested matching search");
        Console.WriteLine($"n = {n},  m = {(options.M.HasValue ? m.ToString() : $"3n-1 = {m}")}");
        Console.WriteLine($"Ölelő élek tiltva: {options.ForbidNested}");
        Console.WriteLine("Színek: piros / kék");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        var instance = CnfInstance.Build(m, n, options.ForbidNested);
        Console.WriteLine($"Élek száma:                 {instance.EdgeCount}");
        Console.WriteLine($"Megengedett M_n-ek száma:   {instance.MatchingsGenerated:N0}");
        Console.WriteLine($"SAT-klauzulák száma:        {instance.ClauseCount:N0}");
        Console.WriteLine();

        var solver = new DpllSolver(instance);
        if (instance.EdgeCount > 0 && !solver.FixVariable(0, true))
        {
            Console.WriteLine("UNSAT (már a szimmetria-rögzítés is ellentmondásos).");
            return;
        }

        var sat = solver.Solve();
        sw.Stop();

        Console.WriteLine($"Eredmény: {(sat ? "SAT – találtam ellenpélda-színezést" : "UNSAT – nincs ilyen színezés")}");
        Console.WriteLine($"Döntési csomópontok:        {solver.DecisionNodes:N0}");
        Console.WriteLine($"Idő:                         {sw.Elapsed}");

        if (!sat) return;

        var coloring = solver.GetAssignment();
        var verifierWitness = ColoringVerifier.FindWitness(instance, coloring);
        if (verifierWitness is not null)
            throw new InvalidOperationException("Belső hiba: a SAT megoldás verifier szerint tiltott witness-t tartalmaz.");

        if (options.ShowColoring || options.OutputFile is not null)
            EmitColoring(instance, coloring, options);

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

    private static void EmitColoring(CnfInstance instance, bool[] coloring, Options options)
    {
        var text = instance.FormatColoring(coloring);
        if (options.ShowColoring)
        {
            Console.WriteLine();
            Console.WriteLine("Teljes K_m élszínezés:");
            Console.WriteLine(text);
        }
        if (options.OutputFile is not null)
        {
            File.WriteAllText(options.OutputFile, text);
            Console.WriteLine($"A teljes élszínezést ide mentettem: {Path.GetFullPath(options.OutputFile)}");
        }
    }
}

internal sealed class Options
{
    public int N { get; private set; } = 3;
    public int? M { get; private set; }
    public long? InspectMatchingIndex { get; private set; }
    public int MaxColorings { get; private set; } = 64;
    public bool ForbidNested { get; private set; } = true;
    public bool Force { get; private set; }
    public bool Help { get; private set; }
    public bool SelfTest { get; private set; }
    public bool RunSatTests { get; private set; } = true;
    public bool RequireNauty { get; private set; }
    public bool UseNauty { get; private set; }
    public bool Verify { get; private set; }
    public bool VerifyAll { get; private set; }
    public bool ShowColoring { get; private set; }
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
                case "-m":
                case "--m":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out var m))
                        throw new ArgumentException("A --m után egész szám kell.");
                    o.M = m;
                    break;
                case "--case":
                case "--case-index":
                case "--inspect-matching":
                    if (i + 1 >= args.Length || !long.TryParse(args[++i], out var caseIndex))
                        throw new ArgumentException("A --case után pozitív egész index kell.");
                    o.InspectMatchingIndex = caseIndex;
                    break;
                case "--max-colorings":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out var maxColorings))
                        throw new ArgumentException("A --max-colorings után egész szám kell.");
                    o.MaxColorings = maxColorings;
                    break;
                case "--allow-nested": o.ForbidNested = false; break;
                case "--force": o.Force = true; break;
                case "--self-test": o.SelfTest = true; break;
                case "--no-sat-tests": o.RunSatTests = false; break;
                case "--require-nauty": o.RequireNauty = true; break;
                case "--nauty": o.UseNauty = true; break;
                case "--verify": o.Verify = true; break;
                case "--verify-all": o.VerifyAll = true; break;
                case "--show-coloring": o.ShowColoring = true; break;
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
        Console.WriteLine("  dotnet run -- -n 3 -m 7");
        Console.WriteLine("  dotnet run -- -n 6 -m 17 --case 123");
        Console.WriteLine("  dotnet run -- -n 6 -m 17 --case 123 --max-colorings 64");
        Console.WriteLine("  dotnet run -- -n 3 -m 7 --show-coloring -o coloring.txt");
        Console.WriteLine("  dotnet run -- -n 3 --nauty");
        Console.WriteLine("  dotnet run -- --verify -n 4 --nauty");
        Console.WriteLine("  dotnet run -- --verify-all");
        Console.WriteLine("  dotnet run -- --self-test --require-nauty");
        Console.WriteLine();
        Console.WriteLine("Kapcsolók:");
        Console.WriteLine("  --n, -n N           a matching mérete (n él)");
        Console.WriteLine("  --m, -m M           a rendezett teljes gráf csúcsainak száma; alapértelmezés: 3n-1");
        Console.WriteLine("  --case K            a K-adik generált matching részleteinek kiírása (1-től indexelve)");
        Console.WriteLine("  --max-colorings K   legfeljebb ennyi piros/kék mintát ír ki a kiválasztott matching éleire (alap: 64)");
        Console.WriteLine("  --allow-nested      ölelő élek engedélyezése");
        Console.WriteLine("  --show-coloring     SAT esetén a teljes K_m élszínezésének kiírása");
        Console.WriteLine("  --output FILE       SAT esetén a teljes élszínezés mentése fájlba");
        Console.WriteLine("  --nauty             SAT megoldás kanonikusítása nauty-val");
        Console.WriteLine("  --verify            DPLL eredmény független solverrel is ellenőrizve");
        Console.WriteLine("  --verify-all        n=2..5 teljes független keresztellenőrzés m=3n-1 mellett");
        Console.WriteLine("  --self-test         visszaellenőrző tesztek futtatása");
        Console.WriteLine("  --no-sat-tests      self-test alatt a SAT regressziókat kihagyja");
        Console.WriteLine("  --require-nauty     self-testben kötelezővé teszi a natív nauty-t");
        Console.WriteLine("  --force             nagyobb keresési tér engedélyezése a biztonsági korlát fölött");
        Console.WriteLine("  --help, -h          segítség");
        Console.WriteLine();
        Console.WriteLine("Megjegyzés: --case egy matchinget választ ki, nem a teljes gráf egy teljes színezését.");
    }
}
