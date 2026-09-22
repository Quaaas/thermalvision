namespace ThermalViewer.Core.Imaging;

/// <summary>
/// False-color lookup tables with 256 entries, as BGRA32 values (0xAARRGGBB — on a
/// little-endian machine that's the byte order B, G, R, A, i.e. Avalonia's Bgra8888).
/// Index 0 = coldest, index 255 = hottest.
/// </summary>
public static class ThermalPalettes
{
    public const int Size = 256;

    private static readonly uint[] IronbowLut = BuildGradient(
        (0.00, 0, 0, 0),
        (0.15, 30, 0, 110),
        (0.35, 130, 0, 150),
        (0.50, 200, 30, 100),
        (0.65, 240, 90, 20),
        (0.80, 255, 170, 0),
        (0.92, 255, 230, 80),
        (1.00, 255, 255, 255));

    private static readonly uint[] GrayscaleLut = BuildGradient(
        (0.00, 0, 0, 0),
        (1.00, 255, 255, 255));

    /// <summary>Classic thermography palette: black → purple → red → yellow → white.</summary>
    public static ReadOnlySpan<uint> Ironbow => IronbowLut;

    /// <summary>White = hot.</summary>
    public static ReadOnlySpan<uint> Grayscale => GrayscaleLut;

    /// <summary>Linear interpolation between color stops (positions 0..1, ascending, first 0 and last 1).</summary>
    internal static uint[] BuildGradient(params (double Position, byte R, byte G, byte B)[] stops)
    {
        var lut = new uint[Size];
        int stop = 0;
        for (int i = 0; i < Size; i++)
        {
            double t = i / (double)(Size - 1);
            while (stop < stops.Length - 2 && t > stops[stop + 1].Position)
            {
                stop++;
            }

            var (p0, r0, g0, b0) = stops[stop];
            var (p1, r1, g1, b1) = stops[stop + 1];
            double f = Math.Clamp((t - p0) / (p1 - p0), 0, 1);

            uint r = (uint)Math.Round(r0 + ((r1 - r0) * f));
            uint g = (uint)Math.Round(g0 + ((g1 - g0) * f));
            uint b = (uint)Math.Round(b0 + ((b1 - b0) * f));
            lut[i] = 0xFF000000u | (r << 16) | (g << 8) | b;
        }

        return lut;
    }
}
