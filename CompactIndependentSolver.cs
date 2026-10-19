using System.Diagnostics;

namespace OrderedMatching;

internal sealed record CompactIndependentResult(
    bool IsSat,
    bool[] Assignment,
    long DecisionNodes,
    long Propagations,
    TimeSpan Elapsed);

/// <summary>
/// Külön implementáció: nem CNF-et kezel, hanem minden matching állapotát tartja fenn.
/// CSR incidence-listákat használ a memóriahatékonyság miatt.
///
/// Fontos invariáns: az Enqueue azonnal frissíti a számlálókat minden olyan matchingnél,
/// amely tartalmazza az élt. Így konfliktus esetén sem marad félig frissített állapot,
/// és a Backtrack minden trail-változást szabályosan vissza tud vonni.
/// </summary>
internal sealed class CompactIndependentSolver
{
    private readonly CompactIndependentMatchingModel _model;
    private readonly int[] _offset;
    private readonly int[] _incidence;
    private readonly int[] _occurrence;
    private readonly byte[] _redCount;
    private readonly byte[] _unassignedCount;
    private readonly byte[] _assignment;
    private readonly int[] _trail;
    private int _trailCount;
    private int _queueHead;

    public long DecisionNodes { get; private set; }
    public long Propagations { get; private set; }

    public CompactIndependentSolver(CompactIndependentMatchingModel model)
    {
        _model = model;
        _assignment = new byte[model.EdgeCount];
        _redCount = new byte[model.MatchingCount];
        _unassignedCount = new byte[model.MatchingCount];
        _trail = new int[model.EdgeCount];
        _occurrence = new int[model.EdgeCount];

        var total = checked(model.MatchingCount * model.MatchingSize);
        _offset = new int[model.EdgeCount + 1];
        for (var m = 0; m < model.MatchingCount; m++)
        {
            foreach (var e in model.Matching(m))
            {
                _offset[e + 1]++;
                _occurrence[e]++;
            }
            _unassignedCount[m] = (byte)model.MatchingSize;
        }

        for (var e = 0; e < model.EdgeCount; e++)
            _offset[e + 1] += _offset[e];

        _incidence = new int[total];
        var cursor = (int[])_offset.Clone();
        for (var m = 0; m < model.MatchingCount; m++)
        foreach (var e in model.Matching(m))
            _incidence[cursor[e]++] = m;
    }

    public CompactIndependentResult Solve(bool fixFirstRed = true)
    {
        var sw = Stopwatch.StartNew();
        if (fixFirstRed && _model.EdgeCount > 0 && !Enqueue(0, 2))
        {
            sw.Stop();
            return new CompactIndependentResult(false, GetAssignment(), DecisionNodes, Propagations, sw.Elapsed);
        }

        var sat = Search();
        sw.Stop();
        return new CompactIndependentResult(sat, GetAssignment(), DecisionNodes, Propagations, sw.Elapsed);
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
        var mark = _trailCount;
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
        var k = _model.MatchingSize;
        while (_queueHead < _trailCount)
        {
            Propagations++;
            var edge = _trail[_queueHead++];

            // The matching counters have already been fully updated by Enqueue().
            // Here we only detect conflicts and derive unit implications.
            for (var p = _offset[edge]; p < _offset[edge + 1]; p++)
            {
                var m = _incidence[p];
                var blueCount = k - _redCount[m] - _unassignedCount[m];
                if (_redCount[m] == k || blueCount == k)
                    return false;

                if (_unassignedCount[m] == 1)
                {
                    var remaining = FindRemaining(m);
                    if (remaining < 0) return false;
                    if (_redCount[m] == k - 1)
                    {
                        if (!Enqueue(remaining, 1)) return false; // force blue
                    }
                    else if (blueCount == k - 1)
                    {
                        if (!Enqueue(remaining, 2)) return false; // force red
                    }
                }
            }
        }
        return true;
    }

    private int FindRemaining(int matchingId)
    {
        foreach (var e in _model.Matching(matchingId))
            if (_assignment[e] == 0) return e;
        return -1;
    }

    private bool Enqueue(int edge, byte value)
    {
        if (_assignment[edge] != 0)
            return _assignment[edge] == value;

        _assignment[edge] = value;
        _trail[_trailCount++] = edge;

        // Update every incident matching atomically with respect to the trail.
        // Never update these counters lazily in Propagate: a conflict may return
        // halfway through the incidence list, leaving Backtrack unable to undo it.
        var red = value == 2;
        for (var p = _offset[edge]; p < _offset[edge + 1]; p++)
        {
            var m = _incidence[p];
            _unassignedCount[m]--;
            if (red) _redCount[m]++;
        }
        return true;
    }

    private void Backtrack(int mark)
    {
        while (_trailCount > mark)
        {
            var edge = _trail[--_trailCount];
            var red = _assignment[edge] == 2;
            for (var p = _offset[edge]; p < _offset[edge + 1]; p++)
            {
                var m = _incidence[p];
                _unassignedCount[m]++;
                if (red) _redCount[m]--;
            }
            _assignment[edge] = 0;
        }
        _queueHead = Math.Min(_queueHead, _trailCount);
    }
}
