namespace OrderedMatching;

/// <summary>
/// Kis, célzott SAT-megoldó kétértékű (piros/kék) edge-coloring feladatra.
/// Watcholt literálokat és DPLL-t használ.
/// </summary>
internal sealed class DpllSolver
{
    private readonly CnfInstance _instance;
    private readonly int _variableCount;
    private readonly int[] _watch0;
    private readonly int[] _watch1;
    private readonly byte[] _assignment; // 0=unassigned, 1=false/kék, 2=true/piros
    private readonly int[] _occurrence;
    private readonly List<int> _trail = new();
    private int _queueHead;

    public long DecisionNodes { get; private set; }

    public DpllSolver(CnfInstance instance)
    {
        _instance = instance;
        _variableCount = instance.EdgeCount;
        _assignment = new byte[_variableCount];
        _watch0 = new int[instance.ClauseCount];
        _watch1 = new int[instance.ClauseCount];
        _occurrence = new int[_variableCount];

        // Módosítható watcher-listák.
        var mutableWatches = new List<int>[2 * _variableCount];
        for (var i = 0; i < mutableWatches.Length; i++)
            mutableWatches[i] = new List<int>();

        for (var c = 0; c < instance.ClauseCount; c++)
        {
            var clause = instance.GetClause(c);
            _watch0[c] = 0;
            _watch1[c] = clause.Length > 1 ? 1 : 0;

            foreach (var lit in clause)
                _occurrence[lit / 2]++;

            mutableWatches[clause[_watch0[c]]].Add(c);
            mutableWatches[clause[_watch1[c]]].Add(c);
        }

        MutableWatchLists = mutableWatches;
    }

    // A tényleges watcher-listák.
    private List<int>[] MutableWatchLists = Array.Empty<List<int>>();

    public bool FixVariable(int variable, bool red)
    {
        return Enqueue(variable, red ? (byte)2 : (byte)1);
    }

    public bool Solve()
    {
        if (!Propagate())
            return false;

        var variable = ChooseVariable();
        if (variable < 0)
            return true;

        DecisionNodes++;
        var baseLevel = _trail.Count;

        // Piros ágat nézzük először.
        if (Enqueue(variable, 2) && Solve())
            return true;
        Backtrack(baseLevel);

        if (Enqueue(variable, 1) && Solve())
            return true;
        Backtrack(baseLevel);

        return false;
    }

    public bool[] GetAssignment()
    {
        var result = new bool[_variableCount];
        for (var i = 0; i < _variableCount; i++)
            result[i] = _assignment[i] == 2;
        return result;
    }

    private int ChooseVariable()
    {
        var best = -1;
        var bestScore = -1;
        for (var v = 0; v < _variableCount; v++)
        {
            if (_assignment[v] != 0)
                continue;
            if (_occurrence[v] > bestScore)
            {
                best = v;
                bestScore = _occurrence[v];
            }
        }
        return best;
    }

    private bool Propagate()
    {
        while (_queueHead < _trail.Count)
        {
            var variable = _trail[_queueHead++];
            var value = _assignment[variable];
            var falseLiteral = value == 2 ? 2 * variable + 1 : 2 * variable;
            var list = MutableWatchLists[falseLiteral];

            var i = 0;
            while (i < list.Count)
            {
                var clauseId = list[i];
                var clause = _instance.GetClause(clauseId);
                var w0 = _watch0[clauseId];
                var w1 = _watch1[clauseId];

                int falseWatch;
                int otherWatch;
                if (clause[w0] == falseLiteral)
                {
                    falseWatch = w0;
                    otherWatch = w1;
                }
                else if (clause[w1] == falseLiteral)
                {
                    falseWatch = w1;
                    otherWatch = w0;
                }
                else
                {
                    // A clause már átkerült másik watch-listára; védekezés inkonzisztens állapot ellen.
                    i++;
                    continue;
                }

                var otherLiteral = clause[otherWatch];
                if (IsTrue(otherLiteral))
                {
                    i++;
                    continue;
                }

                var replacement = -1;
                for (var k = 0; k < clause.Length; k++)
                {
                    if (k == falseWatch || k == otherWatch)
                        continue;
                    if (!IsFalse(clause[k]))
                    {
                        replacement = k;
                        break;
                    }
                }

                if (replacement >= 0)
                {
                    if (falseWatch == w0)
                        _watch0[clauseId] = replacement;
                    else
                        _watch1[clauseId] = replacement;

                    var newLiteral = clause[replacement];
                    list[i] = list[^1];
                    list.RemoveAt(list.Count - 1);
                    MutableWatchLists[newLiteral].Add(clauseId);
                    continue;
                }

                if (IsFalse(otherLiteral))
                    return false;

                // Az otherLiteral unitként kikényszeríthető.
                if (!AssignLiteral(otherLiteral))
                    return false;
                i++;
            }
        }

        return true;
    }

    private bool Enqueue(int variable, byte value)
    {
        if (_assignment[variable] != 0)
            return _assignment[variable] == value;

        _assignment[variable] = value;
        _trail.Add(variable);
        return true;
    }

    private bool AssignLiteral(int literal)
    {
        var variable = literal / 2;
        var value = (literal & 1) == 0 ? (byte)2 : (byte)1;
        return Enqueue(variable, value);
    }

    private bool IsTrue(int literal)
    {
        var value = _assignment[literal / 2];
        if (value == 0)
            return false;
        return (literal & 1) == 0 ? value == 2 : value == 1;
    }

    private bool IsFalse(int literal)
    {
        var value = _assignment[literal / 2];
        if (value == 0)
            return false;
        return (literal & 1) == 0 ? value == 1 : value == 2;
    }

    private void Backtrack(int trailLength)
    {
        while (_trail.Count > trailLength)
        {
            var variable = _trail[^1];
            _trail.RemoveAt(_trail.Count - 1);
            _assignment[variable] = 0;
        }
        _queueHead = Math.Min(_queueHead, trailLength);
    }
}
