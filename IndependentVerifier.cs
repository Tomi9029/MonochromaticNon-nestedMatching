using System.Diagnostics;

namespace OrderedMatching;

internal sealed record IndependentSolveResult(bool IsSat, bool[] Assignment, long DecisionNodes, TimeSpan Elapsed);

/// <summary>
/// A DPLL solvertől független kereső. Nem CNF-listákat és watchereket használ,
/// hanem közvetlenül a nem-ölelő matchingek állapotát tartja fenn.
/// </summary>
internal sealed class IndependentSearchSolver
{
    private readonly IndependentMatchingModel _model;
    private readonly int[][] _incidence;
    private readonly int[] _redCount;
    private readonly int[] _blueCount;
    private readonly int[] _unassignedCount;
    private readonly byte[] _assignment; // 0 ismeretlen, 1 kék, 2 piros
    private readonly int[] _occurrence;
    private readonly List<int> _trail = new();
    private int _queueHead;

    public long DecisionNodes { get; private set; }

    public IndependentSearchSolver(IndependentMatchingModel model)
    {
        _model = model;
        _assignment = new byte[model.EdgeCount];
        _redCount = new int[model.Matchings.Count];
        _blueCount = new int[model.Matchings.Count];
        _unassignedCount = Enumerable.Repeat(model.MatchingSize, model.Matchings.Count).ToArray();
        _occurrence = new int[model.EdgeCount];

        var temp = new List<int>[model.EdgeCount];
        for (var i = 0; i < temp.Length; i++) temp[i] = new List<int>();
        for (var m = 0; m < model.Matchings.Count; m++)
        {
            foreach (var e in model.Matchings[m].EdgeIds)
            {
                temp[e].Add(m);
                _occurrence[e]++;
            }
        }
        _incidence = temp.Select(x => x.ToArray()).ToArray();
    }

    public bool FixVariable(int variable, bool red) => Enqueue(variable, red ? (byte)2 : (byte)1);

    public IndependentSolveResult Solve(bool useColorSymmetry = true)
    {
        var sw = Stopwatch.StartNew();
        if (useColorSymmetry && _model.EdgeCount > 0 && !FixVariable(0, true))
            return new IndependentSolveResult(false, GetAssignment(), DecisionNodes, sw.Elapsed);

        var sat = Search();
        sw.Stop();
        return new IndependentSolveResult(sat, GetAssignment(), DecisionNodes, sw.Elapsed);
    }

    public bool[] GetAssignment()
    {
        var result = new bool[_assignment.Length];
        for (var i = 0; i < result.Length; i++) result[i] = _assignment[i] == 2;
        return result;
    }

    private bool Search()
    {
        if (!Propagate()) return false;
        var variable = ChooseVariable();
        if (variable < 0) return true;

        DecisionNodes++;
        var mark = _trail.Count;
        if (Enqueue(variable, 2) && Search()) return true;
        Backtrack(mark);

        if (Enqueue(variable, 1) && Search()) return true;
        Backtrack(mark);
        return false;
    }

    private int ChooseVariable()
    {
        var best = -1;
        var score = -1;
        for (var e = 0; e < _assignment.Length; e++)
        {
            if (_assignment[e] != 0) continue;
            if (_occurrence[e] > score)
            {
                score = _occurrence[e];
                best = e;
            }
        }
        return best;
    }

    private bool Propagate()
    {
        while (_queueHead < _trail.Count)
        {
            var edgeId = _trail[_queueHead++];
            var red = _assignment[edgeId] == 2;

            foreach (var mid in _incidence[edgeId])
            {
                _unassignedCount[mid]--;
                if (red) _redCount[mid]++; else _blueCount[mid]++;

                var k = _model.MatchingSize;
                if (_redCount[mid] == k || _blueCount[mid] == k)
                    return false;

                if (_unassignedCount[mid] == 1)
                {
                    var remaining = FindRemainingEdge(_model.Matchings[mid].EdgeIds);
                    if (remaining < 0) return false;

                    if (_redCount[mid] == k - 1)
                    {
                        if (!Enqueue(remaining, 1)) return false; // blue
                    }
                    else if (_blueCount[mid] == k - 1)
                    {
                        if (!Enqueue(remaining, 2)) return false; // red
                    }
                }
            }
        }
        return true;
    }

    private int FindRemainingEdge(int[] edges)
    {
        foreach (var e in edges)
            if (_assignment[e] == 0) return e;
        return -1;
    }

    private bool Enqueue(int variable, byte value)
    {
        if (_assignment[variable] != 0)
            return _assignment[variable] == value;
        _assignment[variable] = value;
        _trail.Add(variable);
        return true;
    }

    private void Backtrack(int mark)
    {
        while (_trail.Count > mark)
        {
            var edgeId = _trail[^1];
            var red = _assignment[edgeId] == 2;
            foreach (var mid in _incidence[edgeId])
            {
                _unassignedCount[mid]++;
                if (red) _redCount[mid]--; else _blueCount[mid]--;
            }
            _assignment[edgeId] = 0;
            _trail.RemoveAt(_trail.Count - 1);
        }
        _queueHead = mark;
    }
}

internal static class IndependentColoringVerifier
{
    public static bool IsAvoiding(IndependentMatchingModel model, bool[] red)
    {
        if (red.Length != model.EdgeCount)
            throw new ArgumentException("Hibás élszínezés-hossz.", nameof(red));

        foreach (var matching in model.Matchings)
        {
            var allRed = true;
            var allBlue = true;
            foreach (var e in matching.EdgeIds)
            {
                allRed &= red[e];
                allBlue &= !red[e];
                if (!allRed && !allBlue) break;
            }
            if (allRed || allBlue) return false;
        }
        return true;
    }

    public static int FindFirstWitness(IndependentMatchingModel model, bool[] red, out bool redWitness)
    {
        if (red.Length != model.EdgeCount)
            throw new ArgumentException("Hibás élszínezés-hossz.", nameof(red));

        for (var i = 0; i < model.Matchings.Count; i++)
        {
            var matching = model.Matchings[i];
            var first = red[matching.EdgeIds[0]];
            if (matching.EdgeIds.All(e => red[e] == first))
            {
                redWitness = first;
                return i;
            }
        }

        redWitness = false;
        return -1;
    }
}
