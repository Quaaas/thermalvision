using ThermalViewer.Core.Imaging;

namespace ThermalViewer.Core.Tests;

public class ThermalPalettesTests
{
    [Fact]
    public void Ironbow_Has256OpaqueEntriesFromBlackToWhite()
    {
        ReadOnlySpan<uint> palette = ThermalPalettes.Ironbow;

        Assert.Equal(ThermalPalettes.Size, palette.Length);
        Assert.Equal(0xFF000000u, palette[0]);
        Assert.Equal(0xFFFFFFFFu, palette[^1]);
        foreach (uint color in palette)
        {
            Assert.Equal(0xFF000000u, color & 0xFF000000u);
        }
    }

    [Fact]
    public void Grayscale_IsLinear()
    {
        Assert.Equal(0xFF808080u, ThermalPalettes.Grayscale[128]);
    }
}

public class ThermalImageRendererTests
{
    [Fact]
    public void FindExtremes_ReturnsValuesAndIndices()
    {
        ushort[] raw = [500, 100, 900, 300];

        RawExtremes extremes = ThermalImageRenderer.FindExtremes(raw);

        Assert.Equal(new RawExtremes(100, 1, 900, 2), extremes);
    }

    [Fact]
    public void Render_MapsRangeOntoPaletteAndClampsOutliers()
    {
        uint[] palette = [10, 20, 30, 40, 50];
        ushort[] raw = [0, 100, 150, 200, 1000];
        var pixels = new uint[raw.Length];

        ThermalImageRenderer.Render(raw, low: 100, high: 200, palette, pixels);

        Assert.Equal(new uint[] { 10, 10, 30, 50, 50 }, pixels);
    }

    [Fact]
    public void Render_DegenerateRange_UsesFirstPaletteEntry()
    {
        uint[] palette = [10, 20];
        ushort[] raw = [5, 5];
        var pixels = new uint[2];

        ThermalImageRenderer.Render(raw, low: 5, high: 5, palette, pixels);

        Assert.Equal(new uint[] { 10, 10 }, pixels);
    }

    [Fact]
    public void Render_DestinationTooSmall_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ThermalImageRenderer.Render(new ushort[4], 0, 1, ThermalPalettes.Ironbow.ToArray(), new uint[3]));
    }
}

public class AutoTemperatureRangeTests
{
    [Fact]
    public void Update_FirstFrame_UsesItsExtremes()
    {
        var range = new AutoTemperatureRange(smoothing: 0.5, minimumSpanRaw: 0);

        Assert.Equal((100.0, 300.0), range.Update(100, 300));
    }

    [Fact]
    public void Update_FollowingFrames_AreSmoothed()
    {
        var range = new AutoTemperatureRange(smoothing: 0.5, minimumSpanRaw: 0);
        range.Update(100, 300);

        Assert.Equal((150.0, 400.0), range.Update(200, 500));
    }

    [Fact]
    public void Update_NarrowScene_IsWidenedToMinimumSpanAroundCenter()
    {
        var range = new AutoTemperatureRange(smoothing: 1, minimumSpanRaw: 128);

        Assert.Equal((1036.0, 1164.0), range.Update(1090, 1110));
    }
}
