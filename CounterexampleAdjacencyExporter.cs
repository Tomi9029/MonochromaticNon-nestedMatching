using System.Text;

namespace OrderedMatching;

/// <summary>
/// Writes each selected counterexample representative as two undirected
/// adjacency-list files: one for red edges and one for blue edges.
/// Every vertex is printed, including isolated vertices (for example "7:").
/// </summary>
internal sealed class CounterexampleAdjacencyExporter
{
    private readonly int _vertexCount;
    private readonly string _directory;
    private readonly long _maxPairsPerCategory;
    private long _nonBlockOrdinal;
    private long _noIntervalSupportOrdinal;
    private long _nonBlockSkipped;
    private long _noIntervalSupportSkipped;

    public string OutputDirectory => _directory;
    public long SavedNonBlockCount => _nonBlockOrdinal;
    public long SavedNoIntervalSupportCount => _noIntervalSupportOrdinal;
    public long SkippedNonBlockCount => _nonBlockSkipped;
    public long SkippedNoIntervalSupportCount => _noIntervalSupportSkipped;
    public long MaxPairsPerCategory => _maxPairsPerCategory;

    public CounterexampleAdjacencyExporter(
        int vertexCount,
        int matchingSize,
        string? baseDirectory,
        long maxPairsPerCategory = 0)
    {
        if (maxPairsPerCategory < 0)
            throw new ArgumentOutOfRangeException(nameof(maxPairsPerCategory));

        _vertexCount = vertexCount;
        _maxPairsPerCategory = maxPairsPerCategory;
        var root = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.CurrentDirectory, "counterexample_exports")
            : Path.GetFullPath(baseDirectory);

        Directory.CreateDirectory(root);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var runName = $"n{matchingSize}_m{vertexCount}_{timestamp}";
        var candidate = Path.Combine(root, runName);
        var suffix = 1;
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(root, $"{runName}_{suffix:D2}");
            suffix++;
        }

        Directory.CreateDirectory(candidate);
        _directory = candidate;
    }

    /// <summary>
    /// Saves a pair of adjacency lists for each selected class. The assignment
    /// array is consumed synchronously because the enumerator reuses its buffer.
    /// </summary>
    public void SaveIfSelected(long representativeIndex, byte[] colors, bool isNonBlock, bool hasNoIntervalSupport)
    {
        if (isNonBlock)
        {
            if (HasCapacity(_nonBlockOrdinal))
            {
                _nonBlockOrdinal++;
                SavePair("non_block", _nonBlockOrdinal, representativeIndex, colors);
                if (_nonBlockOrdinal == 1 || _nonBlockOrdinal % 1_000 == 0)
                    Console.WriteLine($"      mentési progress (nem-blokkos): {_nonBlockOrdinal:N0} pár...");
            }
            else
            {
                _nonBlockSkipped++;
            }
        }

        if (hasNoIntervalSupport)
        {
            if (HasCapacity(_noIntervalSupportOrdinal))
            {
                _noIntervalSupportOrdinal++;
                SavePair("no_interval_support", _noIntervalSupportOrdinal, representativeIndex, colors);
                if (_noIntervalSupportOrdinal == 1 || _noIntervalSupportOrdinal % 1_000 == 0)
                    Console.WriteLine($"      mentési progress (nincs intervallumos support): {_noIntervalSupportOrdinal:N0} pár...");
            }
            else
            {
                _noIntervalSupportSkipped++;
            }
        }
    }

    private bool HasCapacity(long savedCount)
        => _maxPairsPerCategory == 0 || savedCount < _maxPairsPerCategory;

    private void SavePair(string category, long categoryOrdinal, long representativeIndex, byte[] colors)
    {
        var stem = $"{category}_{categoryOrdinal:D6}_representative_{representativeIndex:D8}";
        var redPath = Path.Combine(_directory, stem + "_red.txt");
        var bluePath = Path.Combine(_directory, stem + "_blue.txt");

        // Each run gets its own directory. CreateNew is an additional safeguard
        // against accidentally overwriting earlier results.
        using (var redStream = new FileStream(redPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var redWriter = new StreamWriter(redStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            WriteAdjacencyList(redWriter, _vertexCount, colors, targetColor: 2);

        using (var blueStream = new FileStream(bluePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var blueWriter = new StreamWriter(blueStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            WriteAdjacencyList(blueWriter, _vertexCount, colors, targetColor: 1);
    }

    private static void WriteAdjacencyList(TextWriter writer, int vertexCount, byte[] colors, byte targetColor)
    {
        var expectedEdgeCount = checked(vertexCount * (vertexCount - 1) / 2);
        if (colors.Length != expectedEdgeCount)
            throw new ArgumentException("A színezés él-tömbjének mérete nem egyezik a csúcsszámmal.", nameof(colors));
        if (targetColor is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(targetColor));

        for (var u = 0; u < vertexCount; u++)
        {
            writer.Write(u + 1);
            writer.Write(':');

            for (var v = 0; v < vertexCount; v++)
            {
                if (u == v) continue;
                var lo = Math.Min(u, v);
                var hi = Math.Max(u, v);
                var edgeId = EdgeIndex(vertexCount, lo, hi);
                if (colors[edgeId] != targetColor) continue;

                writer.Write(' ');
                writer.Write(v + 1);
            }

            writer.WriteLine();
        }
    }

    private static int EdgeIndex(int vertexCount, int u, int v)
    {
        // Edges are stored row-wise: (0,1),(0,2),...,(1,2),...
        return checked(u * (2 * vertexCount - u - 1) / 2 + (v - u - 1));
    }

    internal static string FormatAdjacencyListForTest(int vertexCount, byte[] colors, byte selectedColor)
    {
        using var writer = new StringWriter();
        WriteAdjacencyList(writer, vertexCount, colors, selectedColor);
        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
