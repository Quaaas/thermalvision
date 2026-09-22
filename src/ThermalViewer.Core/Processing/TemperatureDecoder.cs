namespace ThermalViewer.Core.Processing;

/// <summary>
/// Converts raw 16-bit sensor values from the temperature block into degrees Celsius.
///
/// Per the P3 reverse-engineering notes (same OEM family) the raw values are in 1/64 Kelvin:
/// °C = raw / 64 − 273.15. Still to be verified on our camera against a reference
/// measurement (e.g. skin ≈ 33 °C, ice water 0 °C, boiling water ≈ 100 °C).
/// Emissivity correction is not applied here.
/// </summary>
public static class TemperatureDecoder
{
    private const double RawUnitsPerKelvin = 64.0;
    private const double KelvinToCelsiusOffset = -273.15;

    /// <summary>
    /// Converts a single raw 16-bit sensor value into degrees Celsius.
    /// </summary>
    public static double ToCelsius(ushort rawValue) =>
        rawValue / RawUnitsPerKelvin + KelvinToCelsiusOffset;

    /// <summary>
    /// Converts an entire raw data block into degrees Celsius, element by element.
    /// </summary>
    public static double[] ToCelsius(ReadOnlySpan<ushort> rawValues)
    {
        var result = new double[rawValues.Length];
        for (int i = 0; i < rawValues.Length; i++)
        {
            result[i] = ToCelsius(rawValues[i]);
        }

        return result;
    }
}
