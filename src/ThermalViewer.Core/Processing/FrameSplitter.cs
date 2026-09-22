using ThermalViewer.Core.Models;

namespace ThermalViewer.Core.Processing;

/// <summary>
/// Splits a raw USB video frame from the camera into its image and (for dual frames)
/// raw-data portion.
///
/// Layout per the P3 reverse-engineering notes (not yet verified against our device):
/// rows 0-191 hold the camera's AGC'd IR image (8-bit brightness in the low byte of each
/// 16-bit value), rows 192-193 metadata, rows 194-385 raw 16-bit temperature values in
/// row-major order (little endian, 1/64 Kelvin).
/// </summary>
public static class FrameSplitter
{
    public const int Width = 256;
    public const int NativeImageHeight = 192;
    public const int MetadataRows = 2;
    public const int ImageHeightWithMetadata = NativeImageHeight + MetadataRows; // 194
    public const int DualFrameHeight = ImageHeightWithMetadata + NativeImageHeight; // 386

    private const int BytesPerYuy2Pixel = 2;

    /// <summary>Exact size in bytes of one raw frame of the given format as sent by the camera.</summary>
    public static int FrameSizeInBytes(ThermalFrameFormat format) => format switch
    {
        ThermalFrameFormat.Image256X194 => Width * ImageHeightWithMetadata * BytesPerYuy2Pixel, // 99328
        ThermalFrameFormat.ImageWithRawTemperature256X386 => Width * DualFrameHeight * BytesPerYuy2Pixel, // 197632
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown frame format."),
    };

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
        int expectedBytes = FrameSizeInBytes(ThermalFrameFormat.Image256X194);
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
        int expectedTotalBytes = FrameSizeInBytes(ThermalFrameFormat.ImageWithRawTemperature256X386);
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
