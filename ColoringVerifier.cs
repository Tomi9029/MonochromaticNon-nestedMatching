namespace OrderedMatching;

internal sealed record MatchingWitness(bool Red, int[] EdgeIds)
{
    public string ColorName => Red ? "piros" : "kék";
}

internal static class ColoringVerifier
{
    public static bool IsValidAvoidingColoring(CnfInstance instance, bool[] red)
    {
        if (red.Length != instance.EdgeCount)
            throw new ArgumentException("Hibás élszínezés-hossz.", nameof(red));

        for (var matching = 0; matching < instance.MatchingsGenerated; matching++)
        {
            var positive = instance.GetClause(2 * matching);
            var negative = instance.GetClause(2 * matching + 1);

            var allBlue = true;
            var allRed = true;
            foreach (var literal in positive)
            {
                var edgeId = literal / 2;
                allBlue &= !red[edgeId];
                allRed &= red[edgeId];
                if (!allBlue && !allRed)
                    break;
            }

            if (allBlue)
                return false;
            if (allRed)
                return false;
        }

        return true;
    }

    public static MatchingWitness? FindWitness(CnfInstance instance, bool[] red)
    {
        if (red.Length != instance.EdgeCount)
            throw new ArgumentException("Hibás élszínezés-hossz.", nameof(red));

        for (var matching = 0; matching < instance.MatchingsGenerated; matching++)
        {
            var clause = instance.GetClause(2 * matching);
            var edges = clause.ToArray();
            var edgeIds = edges.Select(l => l / 2).ToArray();
            var firstColor = red[edgeIds[0]];

            if (edgeIds.All(id => red[id] == firstColor))
                return new MatchingWitness(firstColor, edgeIds);
        }

        return null;
    }

    public static void AssertWitnessIsLegal(CnfInstance instance, MatchingWitness witness)
    {
        if (witness.EdgeIds.Length != instance.MatchingSize)
            throw new InvalidOperationException("A witness nem megfelelő méretű matching.");

        var used = new HashSet<int>();
        var edges = witness.EdgeIds.Select(instance.GetEdge).ToArray();

        foreach (var (u, v) in edges)
        {
            if (!used.Add(u) || !used.Add(v))
                throw new InvalidOperationException("A witness nem párosítás: közös csúcs szerepel.");
        }

        for (var i = 0; i < edges.Length; i++)
        for (var j = i + 1; j < edges.Length; j++)
        {
            if (NestedRelations.IsNested(edges[i], edges[j]))
                throw new InvalidOperationException("A witness ölelő éleket tartalmaz.");
        }
    }
}
