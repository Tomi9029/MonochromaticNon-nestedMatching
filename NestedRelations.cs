namespace OrderedMatching;

internal static class NestedRelations
{
    public static bool IsNested((int U, int V) a, (int U, int V) b)
    {
        var a1 = Math.Min(a.U, a.V);
        var a2 = Math.Max(a.U, a.V);
        var b1 = Math.Min(b.U, b.V);
        var b2 = Math.Max(b.U, b.V);

        if (a1 > b1)
            (a1, a2, b1, b2) = (b1, b2, a1, a2);

        // a1 < b1 < b2 < a2
        return a1 < b1 && b2 < a2;
    }
}
