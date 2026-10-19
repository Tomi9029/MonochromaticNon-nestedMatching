namespace OrderedMatching;

/// <summary>
/// Memóriatakarékos, két-watchos SAT/DPLL solver.
///
/// A 2M klauzula implicit módon van tárolva: minden nem-nested matchingből
/// egy csupa-red és egy csupa-blue tiltó klauzula készül.
///
/// A korábbi List<int>[] watcher-struktúra helyett lapos, intrusive láncolt
/// watch-listákat használunk, így n=6-nál nem keletkezik több százezer külön
/// dinamikusan növekvő List-kapacitás.
/// </summary>
internal sealed class CompactCnfSolver
{
    private readonly CompactMatchingModel _model;
    private readonly int _k;
    private readonly int _variableCount;
    private readonly int _clauseCount;

    private readonly byte[] _assignment; // 0 unknown, 1 blue, 2 red
    private readonly byte[] _watch0;
    private readonly byte[] _watch1;

    // Static branching score.
    private readonly int[] _occurrence;

    // One intrusive linked-list node per watch occurrence.
    // node = 2*clauseId + side (0/1).
    private readonly int[] _watchHead;
    private readonly int[] _watchNext;
    private readonly int[] _watchPrev;

    private readonly int[] _trail;
    private int _trailCount;
    private int _queueHead;

    private readonly bool _showProgress;
    private long _nextProgressAt;
    private long _lastProgressTicks;
    private readonly System.Diagnostics.Stopwatch _progressClock = System.Diagnostics.Stopwatch.StartNew();

    public long DecisionNodes { get; private set; }
    public long Propagations { get; private set; }
    public int ClauseCount => _clauseCount;

    public CompactCnfSolver(CompactMatchingModel model, bool showProgress = true)
    {
        _model = model;
        _k = model.MatchingSize;
        _variableCount = model.EdgeCount;
        _clauseCount = checked(2 * model.MatchingCount);
        _showProgress = showProgress;
        _nextProgressAt = 1_000_000;

        if (_k < 2 || _k > 255)
            throw new ArgumentException("A solver 2..255 élű matchingre készült.");

        _assignment = new byte[_variableCount];
        _watch0 = new byte[_clauseCount];
        _watch1 = new byte[_clauseCount];
        _occurrence = new int[_variableCount];

        _watchHead = new int[2 * _variableCount];
        Array.Fill(_watchHead, -1);
        // Két watch node tartozik minden klauzulához (side = 0/1),
        // ezért a node tömbök mérete 2 * clauseCount.
        _watchNext = new int[2 * _clauseCount];
        _watchPrev = new int[2 * _clauseCount];
        Array.Fill(_watchNext, -1);
        Array.Fill(_watchPrev, -1);

        for (var m = 0; m < model.MatchingCount; m++)
        {
            var baseIndex = m * _k;
            for (var j = 0; j < _k; j++)
                _occurrence[model.EdgeAtFlatIndex(baseIndex + j)]++;

            var positiveClause = 2 * m;
            var negativeClause = positiveClause + 1;
            _watch0[positiveClause] = 0;
            _watch1[positiveClause] = 1;
            _watch0[negativeClause] = 0;
            _watch1[negativeClause] = 1;

            LinkWatch(2 * positiveClause, Literal(positiveClause, 0));
            LinkWatch(2 * positiveClause + 1, Literal(positiveClause, 1));
            LinkWatch(2 * negativeClause, Literal(negativeClause, 0));
            LinkWatch(2 * negativeClause + 1, Literal(negativeClause, 1));
        }

        _trail = new int[_variableCount];
    }

    public bool FixVariable(int edgeId, bool red)
        => Enqueue(edgeId, red ? (byte)2 : (byte)1);

    public bool[] GetAssignment()
    {
        var result = new bool[_assignment.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = _assignment[i] == 2; // true = red, false = blue
        return result;
    }

    public bool Solve()
    {
        if (!Propagate()) return false;

        var variable = ChooseVariable();
        if (variable < 0) return true;

        DecisionNodes++;
        ReportProgressIfNeeded();

        var mark = _trailCount;

        // Try red first. The first variable is fixed separately to remove
        // global color-swap symmetry.
        if (Enqueue(variable, 2) && Solve()) return true;
        Backtrack(mark);

        if (Enqueue(variable, 1) && Solve()) return true;
        Backtrack(mark);
        return false;
    }

    private int ChooseVariable()
    {
        var best = -1;
        var bestScore = -1;
        for (var v = 0; v < _variableCount; v++)
        {
            if (_assignment[v] != 0) continue;
            var score = _occurrence[v];
            if (score > bestScore)
            {
                best = v;
                bestScore = score;
            }
        }
        return best;
    }

    private bool Propagate()
    {
        while (_queueHead < _trailCount)
        {
            Propagations++;
            if ((Propagations & ((1L << 18) - 1)) == 0)
                ReportProgressIfNeeded();

            var variable = _trail[_queueHead++];
            var value = _assignment[variable];
            var falseLiteral = value == 2 ? 2 * variable + 1 : 2 * variable;

            var node = _watchHead[falseLiteral];
            while (node >= 0)
            {
                // Save old next before any unlink/relink operation.
                var nextNode = _watchNext[node];
                var clauseId = node >> 1;
                var side = node & 1;

                var falseWatch = side == 0 ? _watch0[clauseId] : _watch1[clauseId];
                if (Literal(clauseId, falseWatch) != falseLiteral)
                {
                    // Defensive only: valid watch lists should never hit this.
                    node = nextNode;
                    continue;
                }

                var otherWatch = side == 0 ? _watch1[clauseId] : _watch0[clauseId];
                var otherLiteral = Literal(clauseId, otherWatch);
                if (IsTrue(otherLiteral))
                {
                    node = nextNode;
                    continue;
                }

                var replacement = -1;
                for (var p = 0; p < _k; p++)
                {
                    if (p == falseWatch || p == otherWatch) continue;
                    var lit = Literal(clauseId, p);
                    if (!IsFalse(lit))
                    {
                        replacement = p;
                        break;
                    }
                }

                if (replacement >= 0)
                {
                    if (side == 0) _watch0[clauseId] = (byte)replacement;
                    else _watch1[clauseId] = (byte)replacement;

                    var newLiteral = Literal(clauseId, replacement);
                    UnlinkWatch(node, falseLiteral);
                    LinkWatch(node, newLiteral);
                    node = nextNode;
                    continue;
                }

                if (IsFalse(otherLiteral)) return false;
                if (!AssignLiteral(otherLiteral)) return false;

                node = nextNode;
            }
        }
        return true;
    }

    private int Literal(int clauseId, int position)
    {
        var matchingId = clauseId >> 1;
        var edgeId = _model.EdgeAtFlatIndex(matchingId * _k + position);
        var negative = (clauseId & 1) != 0;
        return 2 * edgeId + (negative ? 1 : 0);
    }

    private void LinkWatch(int node, int literal)
    {
        var head = _watchHead[literal];
        _watchPrev[node] = -1;
        _watchNext[node] = head;
        if (head >= 0) _watchPrev[head] = node;
        _watchHead[literal] = node;
    }

    private void UnlinkWatch(int node, int literal)
    {
        var prev = _watchPrev[node];
        var next = _watchNext[node];
        if (prev >= 0) _watchNext[prev] = next;
        else _watchHead[literal] = next;
        if (next >= 0) _watchPrev[next] = prev;
        _watchPrev[node] = -1;
        _watchNext[node] = -1;
    }

    private bool Enqueue(int variable, byte value)
    {
        if (_assignment[variable] != 0)
            return _assignment[variable] == value;
        _assignment[variable] = value;
        _trail[_trailCount++] = variable;
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
        if (value == 0) return false;
        return (literal & 1) == 0 ? value == 2 : value == 1;
    }

    private bool IsFalse(int literal)
    {
        var value = _assignment[literal / 2];
        if (value == 0) return false;
        return (literal & 1) == 0 ? value == 1 : value == 2;
    }

    private void Backtrack(int mark)
    {
        while (_trailCount > mark)
        {
            var variable = _trail[--_trailCount];
            _assignment[variable] = 0;
        }
        _queueHead = Math.Min(_queueHead, _trailCount);
    }

    private void ReportProgressIfNeeded()
    {
        if (!_showProgress) return;
        if (DecisionNodes < _nextProgressAt && _progressClock.ElapsedTicks - _lastProgressTicks < StopwatchTicksPerSecond * 5)
            return;

        _lastProgressTicks = _progressClock.ElapsedTicks;
        _nextProgressAt = Math.Max(_nextProgressAt + 1_000_000, DecisionNodes + 1_000_000);
        Console.WriteLine($"      progress: decisions={DecisionNodes:N0}, propagations={Propagations:N0}, elapsed={_progressClock.Elapsed}");
    }

    private static long StopwatchTicksPerSecond => System.Diagnostics.Stopwatch.Frequency;
}
