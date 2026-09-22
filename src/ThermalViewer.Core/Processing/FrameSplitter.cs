using ThermalViewer.Core.Models;

namespace ThermalViewer.Core.Processing;

/// <summary>
/// Splits a raw USB video frame from the camera into its image and (for dual frames)
/// raw-data portion.
///
/// Layout per the project status notes (not yet verified against the device, see
/// README/status document): in the 256x386 dual frame, a plain 256x192 YUY2 image sits
/// on top, followed by a block of raw 16-bit temperature values in row-major order.
/// </summary>
public static class FrameSplitter
{
    public const int Width = 256;
    public const int NativeImageHeight = 192;
    public const int MetadataRows = 2;
    public const int ImageHeightWithMetadata = NativeImageHeight + MetadataRows; // 194

    private const int BytesPerYuy2Pixel = 2;

    /// <summary>
    /// Splits a raw frame buffer according to the reported format.
    /// </summary>
    /// <exception cref="ArgumentException">If the buffer size doesn't match the format.</exception>
    public static ThermalFrame Split(ThermalFrameFormat format, ReadOnlyMemory<byte> rawFrame)
    {
        return format switch
        {
            ThermalFrameFormat.Image256X194 => SplitImageOnly(rawFrame),
            ThermalFrameFormat.ImageWithRawTemperature256X386 => SplitImageWithTemperature(rawFrame),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown frame format."),
        };
    }

    private static ThermalFrame SplitImageOnly(ReadOnlyMemory<byte> rawFrame)
    {
        int expectedBytes = Width * ImageHeightWithMetadata * BytesPerYuy2Pixel;
        if (rawFrame.Length != expectedBytes)
        {
            throw new ArgumentException(
                $"Unexpected buffer size for {nameof(ThermalFrameFormat.Image256X194)}: " +
                $"expected {expectedBytes} bytes, got {rawFrame.Length} bytes.",
                nameof(rawFrame));
        }

        return new ThermalFrame(
            ThermalFrameFormat.Image256X194,
            Width,
            ImageHeightWithMetadata,
            rawFrame,
            RawTemperatureData: null);
    }

    private static ThermalFrame SplitImageWithTemperature(ReadOnlyMemory<byte> rawFrame)
    {
        int imageBytes = Width * ImageHeightWithMetadata * BytesPerYuy2Pixel;
        int expectedTotalBytes = Width * 386 * BytesPerYuy2Pixel; // per status doc: max. frame buffer 197632 bytes
        if (rawFrame.Length != expectedTotalBytes)
        {
            throw new ArgumentException(
                $"Unexpected buffer size for {nameof(ThermalFrameFormat.ImageWithRawTemperature256X386)}: " +
                $"expected {expectedTotalBytes} bytes, got {rawFrame.Length} bytes.",
                nameof(rawFrame));
        }

        ReadOnlyMemory<byte> imagePart = rawFrame[..imageBytes];
        ReadOnlyMemory<byte> rawTempBytes = rawFrame[imageBytes..];

        // TODO: byte layout (endianness, possibly its own metadata rows within the raw block)
        // is not yet verified — see "Open next steps" in the project status document.
        ushort[] rawTempValues = new ushort[rawTempBytes.Length / 2];
        var span = rawTempBytes.Span;
        for (int i = 0; i < rawTempValues.Length; i++)
        {
            rawTempValues[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(i * 2, 2));
        }

        return new ThermalFrame(
            ThermalFrameFormat.ImageWithRawTemperature256X386,
            Width,
            ImageHeightWithMetadata,
            imagePart,
            rawTempValues);
    }
}
