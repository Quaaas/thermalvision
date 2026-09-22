namespace ThermalViewer.Core.Processing;

/// <summary>
/// Converts raw 16-bit sensor values from the temperature block into degrees Celsius.
///
/// WARNING: the actual conversion formula (scale, offset, possibly a calibration register)
/// for this camera is NOT yet verified (see "Open next steps" in the project status
/// document). The current placeholder assumes a linear scale with a fixed factor/offset,
/// as seen in several InfiRay P2 Pro clones, and must be calibrated against real
/// measurements once raw data from the device is available.
/// </summary>
public static class TemperatureDecoder
{
    // TODO(calibration): placeholder values, verify against a reference measurement
    // (e.g. water bath + reference thermometer).
    private const double PlaceholderScale = 0.04; // °C per LSB
    private const double PlaceholderOffsetCelsius = -273.15;

    /// <summary>
    /// Converts a single raw 16-bit sensor value into degrees Celsius.
    /// </summary>
    public static double ToCelsius(ushort rawValue) =>
        rawValue * PlaceholderScale + PlaceholderOffsetCelsius;

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
