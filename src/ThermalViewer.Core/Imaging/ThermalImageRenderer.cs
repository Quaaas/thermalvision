namespace ThermalViewer.Core.Imaging;

/// <summary>Coldest and hottest raw value in a frame, with their pixel indices.</summary>
public readonly record struct RawExtremes(ushort Min, int MinIndex, ushort Max, int MaxIndex);

/// <summary>
/// Turns a block of raw 16-bit temperature values into false-color pixels. Works on raw
/// values (not °C), so it doesn't depend on the temperature formula being right.
/// </summary>
public static class ThermalImageRenderer
{
    public static RawExtremes FindExtremes(ReadOnlySpan<ushort> raw)
    {
        if (raw.IsEmpty)
        {
            throw new ArgumentException("Raw data must not be empty.", nameof(raw));
        }

        ushort min = ushort.MaxValue, max = ushort.MinValue;
        int minIndex = 0, maxIndex = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            ushort value = raw[i];
            if (value < min)
            {
                min = value;
                minIndex = i;
            }

            if (value > max)
            {
                max = value;
                maxIndex = i;
            }
        }

        return new RawExtremes(min, minIndex, max, maxIndex);
    }

    /// <summary>
    /// Maps every raw value linearly from [<paramref name="low"/>, <paramref name="high"/>]
    /// onto the palette (values outside are clamped) and writes one BGRA32 pixel per value.
    /// </summary>
    public static void Render(
        ReadOnlySpan<ushort> raw, double low, double high, ReadOnlySpan<uint> palette, Span<uint> destination)
    {
        if (destination.Length < raw.Length)
        {
            throw new ArgumentException(
                $"Destination holds {destination.Length} pixels, need {raw.Length}.", nameof(destination));
        }

        if (palette.IsEmpty)
        {
            throw new ArgumentException("Palette must not be empty.", nameof(palette));
        }

        int last = palette.Length - 1;
        double scale = high > low ? last / (high - low) : 0;
        for (int i = 0; i < raw.Length; i++)
        {
            double position = (raw[i] - low) * scale;
            int index = position <= 0 ? 0 : position >= last ? last : (int)position;
            destination[i] = palette[index];
        }
    }
}
