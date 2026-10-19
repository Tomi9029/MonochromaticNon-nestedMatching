using System.Numerics;

namespace OrderedMatching;

internal static class MatchingCountMath
{
    public static BigInteger CountNonNested(int vertexCount, int matchingSize)
    {
        if (vertexCount < 0 || matchingSize < 0 || 2L * matchingSize > vertexCount)
            return BigInteger.Zero;
        return Binomial(vertexCount, 2 * matchingSize) * Catalan(matchingSize);
    }

    public static BigInteger CountAll(int vertexCount, int matchingSize)
    {
        if (vertexCount < 0 || matchingSize < 0 || 2L * matchingSize > vertexCount)
            return BigInteger.Zero;
        var result = Binomial(vertexCount, 2 * matchingSize);
        for (var i = 1; i <= matchingSize; i++)
            result *= 2 * i - 1;
        return result;
    }

    public static BigInteger Binomial(int n, int k)
    {
        if (n < 0 || k < 0 || k > n) return BigInteger.Zero;
        k = Math.Min(k, n - k);
        BigInteger result = BigInteger.One;
        for (var i = 1; i <= k; i++)
        {
            result *= n - k + i;
            result /= i;
        }
        return result;
    }

    public static BigInteger Catalan(int n)
    {
        if (n < 0) return BigInteger.Zero;
        return Binomial(2 * n, n) / (n + 1);
    }
}
