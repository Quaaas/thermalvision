using ThermalViewer.Core.Models;
using ThermalViewer.Core.Processing;
using Xunit;

namespace ThermalViewer.Core.Tests;

public class FrameSplitterTests
{
    [Theory]
    [InlineData(ThermalFrameFormat.Image256X194, 99_328)]
    [InlineData(ThermalFrameFormat.ImageWithRawTemperature256X386, 197_632)]
    public void FrameSizeInBytes_MatchesUvcFrameBufferSize(ThermalFrameFormat format, int expected)
    {
        Assert.Equal(expected, FrameSplitter.FrameSizeInBytes(format));
    }

    [Fact]
    public void Split_ImageOnlyFormat_ThrowsOnWrongBufferSize()
    {
        var tooShort = new byte[10];

        Assert.Throws<ArgumentException>(() =>
            FrameSplitter.Split(ThermalFrameFormat.Image256X194, tooShort));
    }

    [Fact]
    public void Split_ImageOnlyFormat_ReturnsFrameWithoutRawTemperature()
    {
        int expectedBytes = FrameSplitter.Width * FrameSplitter.ImageHeightWithMetadata * 2;
        var buffer = new byte[expectedBytes];

        ThermalFrame frame = FrameSplitter.Split(ThermalFrameFormat.Image256X194, buffer);

        Assert.Equal(FrameSplitter.Width, frame.Width);
        Assert.Equal(FrameSplitter.ImageHeightWithMetadata, frame.ImageHeight);
        Assert.Null(frame.RawTemperatureData);
    }

    [Fact]
    public void Split_DoubleFrameFormat_SplitsImageAndRawTemperatureBlocks()
    {
        int totalBytes = FrameSplitter.Width * 386 * 2;
        var buffer = new byte[totalBytes];

        // Mark the expected raw-data region with a known value (0x1234, little-endian) to
        // confirm the correct part of the buffer is picked up as the temperature block.
        int imageBytes = FrameSplitter.Width * FrameSplitter.ImageHeightWithMetadata * 2;
        buffer[imageBytes] = 0x34;
        buffer[imageBytes + 1] = 0x12;

        ThermalFrame frame = FrameSplitter.Split(ThermalFrameFormat.ImageWithRawTemperature256X386, buffer);

        Assert.NotNull(frame.RawTemperatureData);
        Assert.Equal(0x1234, frame.RawTemperatureData!.Value.Span[0]);
    }
}

public class TemperatureDecoderTests
{
    [Fact]
    public void ToCelsius_IsMonotonicallyIncreasingWithRawValue()
    {
        double lower = TemperatureDecoder.ToCelsius(1000);
        double higher = TemperatureDecoder.ToCelsius(2000);

        Assert.True(higher > lower);
    }

    [Theory]
    [InlineData(17482, 0.0)] // 273.15 K * 64 = 17481.6
    [InlineData(19082, 25.0)] // 298.15 K * 64 = 19081.6
    [InlineData(23882, 100.0)] // 373.15 K * 64 = 23881.6
    public void ToCelsius_UsesOneSixtyFourthKelvinUnits(int raw, double expectedCelsius)
    {
        Assert.Equal(expectedCelsius, TemperatureDecoder.ToCelsius((ushort)raw), precision: 1);
    }
}
