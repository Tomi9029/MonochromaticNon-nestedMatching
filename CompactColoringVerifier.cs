namespace OrderedMatching;

/// <summary>Directly checks a complete edge coloring against compact matching sets.</summary>
internal static class CompactColoringVerifier
{
    public static bool IsAvoiding(CompactMatchingModel model, bool[] red)
    {
        if (red.Length != model.EdgeCount)
            throw new ArgumentException("Hibás élszínezés-hossz.", nameof(red));

        for (var matchingId = 0; matchingId < model.MatchingCount; matchingId++)
        {
            var allRed = true;
            var allBlue = true;
            foreach (var edge in model.Matching(matchingId))
            {
                allRed &= red[edge];
                allBlue &= !red[edge];
                if (!allRed && !allBlue) break;
            }
            if (allRed || allBlue) return false;
        }
        return true;
    }

    public static bool IsAvoiding(CompactIndependentMatchingModel model, bool[] red)
    {
        if (red.Length != model.EdgeCount)
            throw new ArgumentException("Hibás élszínezés-hossz.", nameof(red));

        for (var matchingId = 0; matchingId < model.MatchingCount; matchingId++)
        {
            var allRed = true;
            var allBlue = true;
            foreach (var edge in model.Matching(matchingId))
            {
                allRed &= red[edge];
                allBlue &= !red[edge];
                if (!allRed && !allBlue) break;
            }
            if (allRed || allBlue) return false;
        }
        return true;
    }

    public static bool[] ClassicalLowerBoundColoring(int vertexCount, int matchingSize)
    {
        if (vertexCount != 3 * matchingSize - 2)
            throw new ArgumentException("A klasszikus alsó korlátú színezéshez m=3n-2 szükséges.");

        var edgeCount = vertexCount * (vertexCount - 1) / 2;
        var red = new bool[edgeCount];
        var cliqueSize = 2 * matchingSize - 1;
        var edgeId = 0;
        for (var u = 0; u < vertexCount; u++)
        for (var v = u + 1; v < vertexCount; v++)
        {
            red[edgeId++] = u < cliqueSize && v < cliqueSize;
        }
        return red;
    }
}
