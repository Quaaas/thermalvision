namespace ThermalViewer.Core.Models;

/// <summary>
/// The two UVC frame formats reported by the camera (FORMAT_UNCOMPRESSED, YUY2 GUID,
/// 16 bit/pixel), both with a fixed frame interval of 25 fps.
/// </summary>
public enum ThermalFrameFormat
{
    /// <summary>
    /// Frame index 1: 256x194 — plain YUY2 image (256x192 native + 2 metadata rows).
    /// Max. frame buffer 99328 bytes.
    /// </summary>
    Image256X194 = 1,

    /// <summary>
    /// Frame index 2: 256x386 — dual frame. YUY2 image on top, raw 16-bit temperature
    /// block stacked below it (a pattern known from InfiRay P2 Pro clones). Max. frame
    /// buffer 197632 bytes.
    /// </summary>
    ImageWithRawTemperature256X386 = 2,
}

/// <summary>
/// A frame already split into its image and (optional) raw-data portion by
/// <see cref="Processing.FrameSplitter"/>, extracted from a raw camera frame.
/// </summary>
/// <param name="Format">The original frame format.</param>
/// <param name="Width">Width of the image portion in pixels (always 256).</param>
/// <param name="ImageHeight">Height of the image portion in pixels (192 native + metadata rows).</param>
/// <param name="Yuy2ImageData">YUY2-encoded image data (2 bytes/pixel).</param>
/// <param name="RawTemperatureData">
/// Raw 16-bit temperature values, if <see cref="Format"/> is
/// <see cref="ThermalFrameFormat.ImageWithRawTemperature256X386"/>, otherwise <c>null</c>.
/// The byte layout and conversion formula are not yet verified
/// (see <see cref="Processing.TemperatureDecoder"/>).
/// </param>
public sealed record ThermalFrame(
    ThermalFrameFormat Format,
    int Width,
    int ImageHeight,
    ReadOnlyMemory<byte> Yuy2ImageData,
    ReadOnlyMemory<ushort>? RawTemperatureData);
