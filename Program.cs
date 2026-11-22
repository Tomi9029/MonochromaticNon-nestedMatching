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

        if (!options.CountCounterexamples &&
            (options.SaveNonBlockCounterexamples || options.SaveNoIntervalSupportCounterexamples || options.CounterexampleSaveDirectory is not null || options.CounterexampleSaveLimitSpecified))
        {
            Console.Error.WriteLine("A --save-non-block, --save-no-interval-support, --save-limit és --save-dir kapcsolók csak --count-counterexamples módban használhatók.");
            Environment.ExitCode = 2;
            return;
        }

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

        if (options.PathExtremal)
        {
            if (options.M.HasValue)
            {
                Console.Error.WriteLine("Path módnál a -n a csúcsok száma; külön -m nem adható meg.");
                Environment.ExitCode = 2;
                return;
            }
            Environment.ExitCode = PathExtremalSearch.Run(
                options.N, options.Force, options.ShowColoring, options.OutputFile);
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

        if (options.CountCounterexamples)
        {
            if (!options.ForbidNested)
            {
                Console.Error.WriteLine("A --count-counterexamples jelenleg a nem-nested matching modellhez használható; vedd le a --allow-nested kapcsolót.");
                Environment.ExitCode = 2;
                return;
            }
            if ((options.CounterexampleSaveDirectory is not null || options.CounterexampleSaveLimitSpecified) &&
                !options.SaveNonBlockCounterexamples && !options.SaveNoIntervalSupportCounterexamples)
            {
                Console.Error.WriteLine("A --save-dir és --save-limit csak a --save-non-block vagy --save-no-interval-support kapcsolóval együtt használható.");
                Environment.ExitCode = 2;
                return;
            }

            Environment.ExitCode = CounterexampleEnumeration.Run(
                m,
                options.N,
                options.CounterexampleLimit,
                options.ShowFirstCounterexamples,
                options.ShowFirstNonBlockCounterexamples,
                options.ShowFirstNoIntervalSupport,
                options.Force,
                options.SaveNonBlockCounterexamples,
                options.SaveNoIntervalSupportCounterexamples,
                options.CounterexampleSaveDirectory,
                options.CounterexampleSaveLimit);
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
    public bool CountCounterexamples { get; private set; }
    public long CounterexampleLimit { get; private set; } = 1000;
    public int ShowFirstCounterexamples { get; private set; }
    public int ShowFirstNonBlockCounterexamples { get; private set; }
    public int ShowFirstNoIntervalSupport { get; private set; }
    public bool SaveNonBlockCounterexamples { get; private set; }
    public bool SaveNoIntervalSupportCounterexamples { get; private set; }
    public string? CounterexampleSaveDirectory { get; private set; }
    public long CounterexampleSaveLimit { get; private set; }
    public bool CounterexampleSaveLimitSpecified { get; private set; }
    public bool PathExtremal { get; private set; }
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
                case "--count-counterexamples": o.CountCounterexamples = true; break;
                case "--limit-counterexamples":
                    if (i + 1 >= args.Length || !long.TryParse(args[++i], out var counterexampleLimit))
                        throw new ArgumentException("A --limit-counterexamples után nemnegatív egész szám kell.");
                    if (counterexampleLimit < 0) throw new ArgumentException("A --limit-counterexamples nem lehet negatív.");
                    o.CounterexampleLimit = counterexampleLimit;
                    break;
                case "--show-first":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out var showFirst))
                        throw new ArgumentException("A --show-first után nemnegatív egész szám kell.");
                    if (showFirst < 0) throw new ArgumentException("A --show-first nem lehet negatív.");
                    o.ShowFirstCounterexamples = showFirst;
                    break;
                case "--show-first-non-block":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out var showFirstNonBlock))
                        throw new ArgumentException("A --show-first-non-block után nemnegatív egész szám kell.");
                    if (showFirstNonBlock < 0) throw new ArgumentException("A --show-first-non-block nem lehet negatív.");
                    o.ShowFirstNonBlockCounterexamples = showFirstNonBlock;
                    break;
                case "--show-first-no-interval-support":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out var showFirstNoIntervalSupport))
                        throw new ArgumentException("A --show-first-no-interval-support után nemnegatív egész szám kell.");
                    if (showFirstNoIntervalSupport < 0) throw new ArgumentException("A --show-first-no-interval-support nem lehet negatív.");
                    o.ShowFirstNoIntervalSupport = showFirstNoIntervalSupport;
                    break;
                case "--save-non-block":
                    o.SaveNonBlockCounterexamples = true;
                    break;
                case "--save-no-interval-support":
                    o.SaveNoIntervalSupportCounterexamples = true;
                    break;
                case "--save-dir":
                    if (i + 1 >= args.Length) throw new ArgumentException("A --save-dir után könyvtárnév kell.");
                    o.CounterexampleSaveDirectory = args[++i];
                    break;
                case "--save-limit":
                    if (i + 1 >= args.Length || !long.TryParse(args[++i], out var saveLimit))
                        throw new ArgumentException("A --save-limit után nemnegatív egész szám kell.");
                    if (saveLimit < 0) throw new ArgumentException("A --save-limit nem lehet negatív.");
                    o.CounterexampleSaveLimit = saveLimit;
                    o.CounterexampleSaveLimitSpecified = true;
                    break;
                case "--path-extremal": o.PathExtremal = true; break;
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
        Console.WriteLine("  dotnet run -- --count-counterexamples -n 6 -m 16 --limit-counterexamples 1000 --show-first 3");
        Console.WriteLine("  dotnet run -- --count-counterexamples -n 6 -m 16 --limit-counterexamples 0");
        Console.WriteLine("  dotnet run -- --count-counterexamples -n 3 -m 7 --limit-counterexamples 0 --show-first-non-block 5");
        Console.WriteLine("  dotnet run -- --count-counterexamples -n 3 -m 7 --limit-counterexamples 0 --show-first-no-interval-support 5");
        Console.WriteLine("  dotnet run -- --count-counterexamples -n 3 -m 7 --limit-counterexamples 0 --save-non-block");
        Console.WriteLine("  dotnet run -- --count-counterexamples -n 3 -m 7 --limit-counterexamples 0 --save-no-interval-support");
        Console.WriteLine("  dotnet run -- --count-counterexamples -n 3 -m 7 --save-non-block --save-limit 100");
        Console.WriteLine("  dotnet run -- --count-counterexamples -n 3 -m 7 --save-non-block --save-dir saved_examples");
        Console.WriteLine("  dotnet run -- --path-extremal -n 6");
        Console.WriteLine("  dotnet run -- --path-extremal -n 8 --show-coloring");
        Console.WriteLine("  dotnet run -- --path-extremal -n 11");
        Console.WriteLine("  dotnet run -- --path-extremal -n 12 --force");
        Console.WriteLine("  dotnet run -- --path-extremal -n 13 --force");
        Console.WriteLine("  dotnet run -- --path-extremal -n 18 --force");
        Console.WriteLine("  dotnet run -- --path-extremal -n 20 --force");
        Console.WriteLine();
        Console.WriteLine("Kapcsolók:");
        Console.WriteLine("  --n, -n N           a matching mérete (n él)");
        Console.WriteLine("  --m, -m M           a rendezett teljes gráf csúcsainak száma; alapértelmezés: 3n-1");
        Console.WriteLine("  --case K            a K-adik generált matching részleteinek kiírása (1-től indexelve)");
        Console.WriteLine("  --max-colorings K   legfeljebb ennyi piros/kék mintát ír ki a kiválasztott matching éleire (alap: 64)");
        Console.WriteLine("  --allow-nested      ölelő élek engedélyezése");
        Console.WriteLine("  --show-coloring     SAT esetén a teljes K_m élszínezésének kiírása");
        Console.WriteLine("  --output FILE       SAT esetén a teljes élszínezés mentése fájlba");
        Console.WriteLine("  --count-counterexamples  az összes ellenpélda-színezés enumerálása és megszámlálása");
        Console.WriteLine("  --limit-counterexamples N  reprezentáns-limit; 0 = korlátlan enumeráció (alap: 1000)");
        Console.WriteLine("  --show-first N      az első N ellenpélda-reprezentáns kiírása");
        Console.WriteLine("  --show-first-non-block N  az első N ellenpélda, amely nem pontos intervallumos K_k/komplement blokk");
        Console.WriteLine("  --show-first-no-interval-support N  az első N ellenpélda, ahol egyik szín supportja sem fér intervallumba");
        Console.WriteLine("  --save-non-block    minden nem pontos intervallumos K_k/komplement ellenpéldát két szomszédsági listás fájlba ment");
        Console.WriteLine("  --save-no-interval-support  azokat menti, ahol egyik szín supportja sem fér k egymást követő csúcsba");
        Console.WriteLine("  --save-limit N      legfeljebb N színezéspár mentése kategóriánként; 0 = korlátlan (alap: 0)");
        Console.WriteLine("  --save-dir DIR      mentési gyökérkönyvtár; alapértelmezés: ./counterexample_exports");
        Console.WriteLine("  A számláló külön kezeli: support-befoglalás, tetszőleges k-halmazos K_k/komplement,");
        Console.WriteLine("  valamint az egymást követő k csúcsból álló blokkot (a feltöltött rajz mintáját).");
        Console.WriteLine("  --path-extremal     leghosszabb monochromatikus non-nested utak globális minimuma; itt -n=csúcsszám");
        Console.WriteLine("                      K2..K7: teljes színezés-enumeráció; K8..K13: explicit SAT;");
        Console.WriteLine("                      K12..K13: --force; K14..K20: lazy SAT + --force; K20 a felső határ.");
        Console.WriteLine("  --nauty             SAT megoldás kanonikusítása nauty-val");
        Console.WriteLine("  --verify            DPLL eredmény független solverrel is ellenőrizve");
        Console.WriteLine("  --verify-all        n=2..5 teljes független keresztellenőrzés m=3n-1 mellett");
        Console.WriteLine("  --self-test         visszaellenőrző tesztek futtatása");
        Console.WriteLine("  --no-sat-tests      self-test alatt a SAT regressziókat kihagyja");
        Console.WriteLine("  --require-nauty     self-testben kötelezővé teszi a natív nauty-t");
        Console.WriteLine("  --force             nagyobb keresési tér engedélyezése a biztonsági korlát fölött");
        Console.WriteLine("  --help, -h          segítség");
        Console.WriteLine();
        Console.WriteLine("Mentésnél minden ellenpélda két fájlt kap (_red.txt és _blue.txt), soronként 'csúcs: szomszédok' formában.");
        Console.WriteLine("A mentési könyvtár n, m és időbélyeg alapján automatikus alkönyvtárba kerül.");
        Console.WriteLine("Megjegyzés: --case egy matchinget választ ki, nem a teljes gráf egy teljes színezését.");
        Console.WriteLine("Megjegyzés: --path-extremal módban -n a rendezett teljes gráf csúcsszáma.");
    }
}
