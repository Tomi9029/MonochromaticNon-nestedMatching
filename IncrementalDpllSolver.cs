namespace OrderedMatching;

/// <summary>
/// Incremental two-color DPLL solver used by the lazy path search.
/// Clauses and watched-literal positions survive between Solve calls; only the
/// current assignment/trail is cleared when a new batch of path cuts is added.
/// </summary>
internal sealed class IncrementalDpllSolver
{
    private readonly int _variableCount;
    private readonly byte[] _assignment; // 0=unassigned, 1=blue/false, 2=red/true
    private readonly int[] _occurrence;
    private readonly List<int[]> _clauses = new();
    private readonly List<int>[] _watchLists;
    private readonly List<int> _watch0 = new();
    private readonly List<int> _watch1 = new();
    private readonly List<int> _trail = new();
    private int _queueHead;

    public long DecisionNodes { get; private set; }
    public int ClauseCount => _clauses.Count;

    public IncrementalDpllSolver(int variableCount)
    {
        if (variableCount < 1)
            throw new ArgumentOutOfRangeException(nameof(variableCount));

        _variableCount = variableCount;
        _assignment = new byte[variableCount];
        _occurrence = new int[variableCount];
        _watchLists = new List<int>[2 * variableCount];
        for (var i = 0; i < _watchLists.Length; i++)
            _watchLists[i] = new List<int>();
    }

    /// <summary>Adds the two valid clauses that forbid an edge set from being monochromatic.</summary>
    public void AddForbiddenPath(IReadOnlyList<int> edgeIds)
    {
        if (_trail.Count != 0 || _queueHead != 0)
            throw new InvalidOperationException("Új útklauzula csak a Solve-körök közötti reset után adható hozzá.");
        if (edgeIds.Count < 1)
            throw new ArgumentException("A tiltott út legalább egy élt tartalmazzon.", nameof(edgeIds));

        var redClause = new int[edgeIds.Count];
        var blueClause = new int[edgeIds.Count];
        for (var i = 0; i < edgeIds.Count; i++)
        {
            var edgeId = edgeIds[i];
            if ((uint)edgeId >= (uint)_variableCount)
                throw new ArgumentOutOfRangeException(nameof(edgeIds), "Az út élindexe kívül esik a gráfon.");
            for (var j = 0; j < i; j++)
                if (edgeIds[j] == edgeId)
                    throw new ArgumentException("Egy út nem tartalmazhat ismétlődő élt.", nameof(edgeIds));

            redClause[i] = 2 * edgeId;
            blueClause[i] = 2 * edgeId + 1;
        }

        AddClause(redClause);
        AddClause(blueClause);
    }

    /// <summary>Clears the previous candidate assignment but preserves all learned clauses and watches.</summary>
    public void ResetForNextSolve()
    {
        Array.Clear(_assignment);
        _trail.Clear();
        _queueHead = 0;
        DecisionNodes = 0;
    }

    public bool FixVariable(int variable, bool red)
    {
        if ((uint)variable >= (uint)_variableCount)
            throw new ArgumentOutOfRangeException(nameof(variable));
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

    private void AddClause(int[] clause)
    {
        if (clause.Length == 0)
            throw new ArgumentException("Üres klauzula nem adható hozzá.", nameof(clause));

        var clauseId = _clauses.Count;
        _clauses.Add(clause);
        var w0 = 0;
        var w1 = clause.Length > 1 ? 1 : 0;
        _watch0.Add(w0);
        _watch1.Add(w1);

        foreach (var literal in clause)
            _occurrence[literal / 2]++;

        _watchLists[clause[w0]].Add(clauseId);
        _watchLists[clause[w1]].Add(clauseId);
    }

    private int ChooseVariable()
    {
        var best = -1;
        var bestScore = -1;
        for (var variable = 0; variable < _variableCount; variable++)
        {
            if (_assignment[variable] != 0)
                continue;
            if (_occurrence[variable] > bestScore)
            {
                best = variable;
                bestScore = _occurrence[variable];
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
            var list = _watchLists[falseLiteral];
            var i = 0;

            while (i < list.Count)
            {
                var clauseId = list[i];
                var clause = _clauses[clauseId];
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
                    // The clause has already moved away from this watch-list.
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
                    _watchLists[newLiteral].Add(clauseId);
                    continue;
                }

                if (IsFalse(otherLiteral))
                    return false;
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
