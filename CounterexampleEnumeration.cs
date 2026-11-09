using System.Numerics;
using System.Text;

namespace OrderedMatching;

internal static class CounterexampleEnumeration
{
    public static int Run(int vertexCount, int matchingSize, long limit, int showFirst, bool force)
    {
        if (matchingSize < 1)
        {
            Console.Error.WriteLine("A matching mérete n pozitív egész kell legyen.");
            return 2;
        }
        if (vertexCount < 0 || vertexCount > 28)
        {
            Console.Error.WriteLine("Az ellenpélda-enumerátor jelenleg 0 <= m <= 28 tartományt támogat.");
            return 2;
        }
        if (limit < 0 || showFirst < 0)
        {
            Console.Error.WriteLine("A limit és a show-first nem lehet negatív.");
            return 2;
        }

        var edgeCount = vertexCount * (vertexCount - 1) / 2;
        var matchingCount = MatchingCountMath.CountNonNested(vertexCount, matchingSize);
        var allColorings = BigInteger.One << edgeCount;
        var supportSize = 2 * matchingSize - 1;

        Console.WriteLine("=== Counterexample enumeration + support analysis ===");
        Console.WriteLine($"matching méret n={matchingSize}, csúcsok m={vertexCount}");
        Console.WriteLine($"Élek: {edgeCount:N0}");
        Console.WriteLine($"Nem-nested M_n-ek: {matchingCount:N0}");
        Console.WriteLine($"Az összes címkézett piros/kék színezés: {allColorings:N0}");
        Console.WriteLine("Számolás: a színcserével ekvivalens párokból az első él piros reprezentánsát vizsgáljuk;");
        Console.WriteLine("a végső címkézett darabszámot kétszerezzük. Csúcsmegfordítást nem azonosítunk.");
        Console.WriteLine();

        if (matchingCount.IsZero)
        {
            Console.WriteLine("Nincs vizsgálandó M_n, ezért minden élszínezés ellenpélda.");
            Console.WriteLine($"Pontos ellenpéldaszám: {allColorings:N0}");
            return 0;
        }

        if (matchingCount > int.MaxValue || matchingCount * matchingSize > int.MaxValue)
        {
            Console.Error.WriteLine($"Túl nagy matchingmodell: {matchingCount:N0} matching.");
            return 2;
        }

        const int safeMatchingLimit = 1_000_000;
        if (matchingCount > safeMatchingLimit && !force)
        {
            Console.Error.WriteLine($"A matchingek száma meghaladja a biztonságos {safeMatchingLimit:N0}-es korlátot. Használd a --force kapcsolót.");
            return 2;
        }

        Console.WriteLine("Matchinghalmaz felépítése...");
        var model = CompactIndependentMatchingModel.Build(vertexCount, matchingSize);
        if (model.MatchingCount != (int)matchingCount)
        {
            Console.Error.WriteLine("A matchinggenerátor darabszáma nem egyezik az elméleti képlettel.");
            return 1;
        }

        Console.WriteLine("Ellenpélda-színezések enumerálása és szerkezeti osztályozása...");
        Console.WriteLine(limit == 0
            ? "Korlát: nincs (a teljes enumerálás potenciálisan nagyon hosszú lehet)."
            : $"Színcsere-reprezentáns limit: {limit:N0}.");
        Console.WriteLine($"Vizsgált szerkezeti feltétel: létezik-e {supportSize} csúcsú S, amelyen kívül nincs S-en belüli színű él?");
        Console.WriteLine("Ekvivalens feltétel: valamelyik szín összes éle legfeljebb 2n-1 csúcsot érint.");
        Console.WriteLine();

        var stats = new CounterexampleSupportStats(vertexCount, supportSize);
        var shown = 0;
        Action<long, byte[]> callback = (index, assignment) =>
        {
            stats.Add(assignment);
            if (index <= showFirst)
            {
                shown++;
                Console.WriteLine($"--- ellenpélda-reprezentáns #{index:N0} (az első él piros) ---");
                Console.WriteLine(FormatColoring(vertexCount, matchingSize, assignment));
                Console.WriteLine(stats.FormatCurrent(assignment));
            }
            if (index % 10_000 == 0)
                Console.WriteLine($"      progress: {index:N0} színcsere-reprezentáns megtalálva...");
        };

        var solver = new CompactIndependentSolver(model);
        var result = solver.EnumerateSolutions(limit, callback, fixFirstRed: edgeCount > 0);
        var colorSwapFactor = edgeCount > 0 ? 2 : 1;
        var labelledCount = new BigInteger(result.RepresentativesFound) * colorSwapFactor;

        Console.WriteLine();
        Console.WriteLine("=== Eredmény ===");
        Console.WriteLine($"Ténylegesen feldolgozott színcsere-reprezentánsok: {result.RepresentativesFound:N0}");
        Console.WriteLine($"Címkézett színezések (színcsere-párokkal):       {labelledCount:N0}");
        Console.WriteLine($"Döntési csomópontok:                            {result.DecisionNodes:N0}");
        Console.WriteLine($"Propagációk:                                    {result.Propagations:N0}");
        Console.WriteLine($"Idő:                                            {result.Elapsed}");

        stats.PrintSummary(colorSwapFactor, limitReached: result.LimitReached);

        if (result.LimitReached)
        {
            Console.WriteLine();
            Console.WriteLine("ENUMERATION: TRUNCATED");
            Console.WriteLine("A számlálások alsó korlátok; a pontos számhoz használd a --limit-counterexamples 0 kapcsolót.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("ENUMERATION: COMPLETE");
        }

        if (showFirst > shown)
            Console.WriteLine($"A kért első {showFirst} helyett csak {shown} reprezentánst találtunk.");
        return 0;
    }

    private static string FormatColoring(int m, int n, byte[] color)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"n={n}, m={m}");
        sb.AppendLine("PIROS:");
        var edgeId = 0;
        for (var u = 0; u < m; u++)
        for (var v = u + 1; v < m; v++, edgeId++)
            if (color[edgeId] == 2) sb.AppendLine($"({u + 1},{v + 1})");
        sb.AppendLine("KÉK:");
        edgeId = 0;
        for (var u = 0; u < m; u++)
        for (var v = u + 1; v < m; v++, edgeId++)
            if (color[edgeId] == 1) sb.AppendLine($"({u + 1},{v + 1})");
        return sb.ToString();
    }
}

/// <summary>
/// Per-counterexample structural classification. The support of a color is
/// the set of vertices incident with at least one edge of that color.
/// A color is confined to a k-set exactly when its support has size <= k.
/// </summary>
internal sealed class CounterexampleSupportStats
{
    private readonly int _m;
    private readonly int _k;
    private readonly bool[] _redSupport;
    private readonly bool[] _blueSupport;

    public long RedConfined { get; private set; }
    public long BlueConfined { get; private set; }
    public long EitherConfined { get; private set; }
    public long BothConfined { get; private set; }
    public long NeitherConfined { get; private set; }
    public long HasContiguousWitness { get; private set; }
    public long HasBothSidesIntervalWitness { get; private set; }
    public long ClassicalCliqueConstruction { get; private set; }
    public BigInteger RedCandidateSetCount { get; private set; }
    public BigInteger BlueCandidateSetCount { get; private set; }

    public CounterexampleSupportStats(int vertexCount, int supportSize)
    {
        _m = vertexCount;
        _k = supportSize;
        _redSupport = new bool[vertexCount];
        _blueSupport = new bool[vertexCount];
    }

    public void Add(byte[] colors)
    {
        Array.Clear(_redSupport);
        Array.Clear(_blueSupport);
        var redEdges = 0;
        var blueEdges = 0;
        var edgeId = 0;
        for (var u = 0; u < _m; u++)
        for (var v = u + 1; v < _m; v++, edgeId++)
        {
            if (colors[edgeId] == 2)
            {
                redEdges++;
                _redSupport[u] = _redSupport[v] = true;
            }
            else if (colors[edgeId] == 1)
            {
                blueEdges++;
                _blueSupport[u] = _blueSupport[v] = true;
            }
            else
            {
                throw new InvalidOperationException("A kész színezésben maradt színezetlen él.");
            }
        }

        var red = GetSupportInfo(_redSupport);
        var blue = GetSupportInfo(_blueSupport);
        var redFits = red.Count <= _k;
        var blueFits = blue.Count <= _k;

        if (redFits)
        {
            RedConfined++;
            RedCandidateSetCount += MatchingCountMath.Binomial(_m - red.Count, _k - red.Count);
        }
        if (blueFits)
        {
            BlueConfined++;
            BlueCandidateSetCount += MatchingCountMath.Binomial(_m - blue.Count, _k - blue.Count);
        }

        if (redFits && blueFits) BothConfined++;
        else if (redFits || blueFits) EitherConfined++;
        else NeitherConfined++;

        if (HasIntervalWitness(red) || HasIntervalWitness(blue))
            HasContiguousWitness++;
        if (HasBothSidesIntervalWitnessForSupport(red) || HasBothSidesIntervalWitnessForSupport(blue))
            HasBothSidesIntervalWitness++;

        var targetCliqueEdges = _k * (_k - 1) / 2;
        if ((red.Count == _k && redEdges == targetCliqueEdges) ||
            (blue.Count == _k && blueEdges == targetCliqueEdges))
            ClassicalCliqueConstruction++;
    }

    public string FormatCurrent(byte[] colors)
    {
        Array.Clear(_redSupport);
        Array.Clear(_blueSupport);
        var edgeId = 0;
        for (var u = 0; u < _m; u++)
        for (var v = u + 1; v < _m; v++, edgeId++)
        {
            if (colors[edgeId] == 2) _redSupport[u] = _redSupport[v] = true;
            else _blueSupport[u] = _blueSupport[v] = true;
        }
        var red = GetSupportInfo(_redSupport);
        var blue = GetSupportInfo(_blueSupport);
        var sb = new StringBuilder();
        sb.AppendLine($"Szerkezeti ellenőrzés (k=2n-1={_k}):");
        sb.AppendLine($"  Piros élek supportja: {red.Count} csúcs");
        sb.AppendLine($"  Kék élek supportja:   {blue.Count} csúcs");
        sb.AppendLine($"  Van megfelelő S:      {(red.Count <= _k || blue.Count <= _k ? "IGEN" : "NEM")}");
        if (red.Count <= _k) sb.AppendLine($"  Piros színhez megfelelő S-k száma: {MatchingCountMath.Binomial(_m - red.Count, _k - red.Count)}");
        if (blue.Count <= _k) sb.AppendLine($"  Kék színhez megfelelő S-k száma:   {MatchingCountMath.Binomial(_m - blue.Count, _k - blue.Count)}");
        return sb.ToString();
    }

    public void PrintSummary(int colorSwapFactor, bool limitReached)
    {
        var suffix = limitReached ? " (alsó korlát)" : string.Empty;
        Console.WriteLine();
        Console.WriteLine($"=== Szerkezeti statisztika: k=2n-1={_k} ===");
        Print("Piros supportja <= k", RedConfined, colorSwapFactor, suffix);
        Print("Kék supportja <= k", BlueConfined, colorSwapFactor, suffix);
        Print("Legalább az egyik szín supportja <= k", EitherConfined, colorSwapFactor, suffix);
        Print("Mindkét szín supportja <= k", BothConfined, colorSwapFactor, suffix);
        Print("Egyik szín supportja sem fér el k csúcson", NeitherConfined, colorSwapFactor, suffix);
        Print("Van egymást követő k csúcsú intervallum-witness", HasContiguousWitness, colorSwapFactor, suffix);
        Print("Van olyan intervallum-witness, amelyen kívül mindkét oldalon marad csúcs", HasBothSidesIntervalWitness, colorSwapFactor, suffix);
        Print("Klasszikus teljes K_k / komplement konstrukció", ClassicalCliqueConstruction, colorSwapFactor, suffix);
        Console.WriteLine($"Piros support által megengedett S-halmazok összesen: {RedCandidateSetCount * colorSwapFactor:N0}{suffix}");
        Console.WriteLine($"Kék support által megengedett S-halmazok összesen:   {BlueCandidateSetCount * colorSwapFactor:N0}{suffix}");
        Console.WriteLine("Megjegyzés: az S-halmazok számánál ugyanaz az S több színezéshez is hozzájárulhat; ez nem színezésszám.");
    }

    private static void Print(string label, long representatives, int factor, string suffix)
        => Console.WriteLine($"{label}: {representatives * factor:N0}{suffix} (színcsere-reprezentáns: {representatives:N0})");

    private (int Count, int Min, int Max) GetSupportInfo(bool[] support)
    {
        var count = 0;
        var min = _m;
        var max = -1;
        for (var v = 0; v < _m; v++)
        {
            if (!support[v]) continue;
            count++;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }
        return (count, min, max);
    }

    private bool HasIntervalWitness((int Count, int Min, int Max) info)
    {
        if (info.Count > _k || _k > _m) return false;
        if (info.Count == 0) return true;
        return info.Max - info.Min + 1 <= _k;
    }

    private bool HasBothSidesIntervalWitnessForSupport((int Count, int Min, int Max) info)
    {
        if (info.Count > _k || _k >= _m) return false;
        if (info.Count == 0) return _m - _k >= 2;
        var low = Math.Max(1, info.Max - _k + 1);
        var high = Math.Min(info.Min, _m - _k - 1);
        return low <= high;
    }
}
