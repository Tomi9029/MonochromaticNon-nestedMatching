using System.Runtime.InteropServices;
using System.Text;

namespace OrderedMatching;

internal sealed record NautyCanonicalResult(string Key, int AutomorphismOrder);

internal static class NautyCanonicalizer
{
    private static readonly object Sync = new();

    public static bool IsAvailable()
    {
        try
        {
            var colors = new byte[1];
            var capacity = CertificateCapacity(2);
            var certificate = new byte[capacity];
            var rc = NautyNative.GetCanonicalCertificate(2, colors, certificate, certificate.Length,
                out _, out _);
            LastLoadError = rc == 0 ? null : $"nauty próbahívás hibakóddal tért vissza: {rc}";
            return rc == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            LastLoadError = ex.ToString();
            return false;
        }
    }

    public static string? LastLoadError { get; private set; }

    public static NautyCanonicalResult Canonicalize(CnfInstance instance, bool[] red)
    {
        if (red.Length != instance.EdgeCount)
            throw new ArgumentException("Hibás élszínezés-hossz.", nameof(red));

        var colors = new byte[red.Length];
        for (var i = 0; i < colors.Length; i++)
            colors[i] = red[i] ? (byte)1 : (byte)0;

        var capacity = CertificateCapacity(instance.VertexCount);
        var certificate = new byte[capacity];
        int length;
        int automorphisms;

        lock (Sync)
        {
            var rc = NautyNative.GetCanonicalCertificate(
                instance.VertexCount,
                colors,
                certificate,
                certificate.Length,
                out length,
                out automorphisms);

            if (rc != 0)
                throw new InvalidOperationException($"A nauty hívás sikertelen (hibakód {rc}).");
        }

        if (length < 0 || length > certificate.Length)
            throw new InvalidOperationException("A nauty érvénytelen tanúsítványhosszt adott vissza.");

        return new NautyCanonicalResult(Convert.ToHexString(certificate, 0, length), automorphisms);
    }

    private static int CertificateCapacity(int m)
    {
        var e = checked(m * (m - 1) / 2);
        var total = checked(m + e);
        var adjacencyBytes = (total * (total - 1) / 2 + 7) / 8;
        return checked(total + adjacencyBytes);
    }
}

internal static class NautyNative
{
    [DllImport("orderednauty", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int GetCanonicalCertificate(
        int vertexCount,
        [In] byte[] edgeColors,
        [Out] byte[] certificate,
        int certificateCapacity,
        out int certificateLength,
        out int automorphismOrder);
}
