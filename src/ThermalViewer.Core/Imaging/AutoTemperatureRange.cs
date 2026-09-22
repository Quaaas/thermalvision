namespace ThermalViewer.Core.Imaging;

/// <summary>
/// Automatic color-scale range (AGC) for the live image: follows each frame's min/max with
/// exponential smoothing, so the colors don't flicker from frame to frame, and enforces a
/// minimum span, so a nearly uniform scene doesn't blow sensor noise up to full contrast.
/// Works in raw units. Not thread-safe — use from one thread.
/// </summary>
public sealed class AutoTemperatureRange
{
    private readonly double _smoothing;
    private readonly double _minimumSpan;
    private double _low;
    private double _high;
    private bool _initialized;

    /// <param name="smoothing">Weight of the newest frame, 0 &lt; smoothing ≤ 1 (1 = no smoothing).</param>
    /// <param name="minimumSpanRaw">Minimum width of the range in raw units (128 = 2 K at 1/64 K per unit).</param>
    public AutoTemperatureRange(double smoothing = 0.2, double minimumSpanRaw = 128)
    {
        if (smoothing is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(smoothing), smoothing, "Must be in (0, 1].");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(minimumSpanRaw);
        _smoothing = smoothing;
        _minimumSpan = minimumSpanRaw;
    }

    /// <summary>Feeds a frame's extremes and returns the range to render with.</summary>
    public (double Low, double High) Update(ushort frameMin, ushort frameMax)
    {
        if (!_initialized)
        {
            _low = frameMin;
            _high = frameMax;
            _initialized = true;
        }
        else
        {
            _low += _smoothing * (frameMin - _low);
            _high += _smoothing * (frameMax - _high);
        }

        double span = _high - _low;
        if (span >= _minimumSpan)
        {
            return (_low, _high);
        }

        double center = (_low + _high) / 2;
        return (center - (_minimumSpan / 2), center + (_minimumSpan / 2));
    }
}
