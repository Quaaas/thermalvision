using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ThermalViewer.App.Rendering;

/// <summary>
/// Copies BGRA32 pixel buffers into Avalonia bitmaps for display. Uses two bitmaps
/// alternately: handing the Image control a different instance each frame makes Avalonia
/// re-render reliably, and the bitmap being written is never the one currently on screen.
/// Must be created and used on the UI thread.
/// </summary>
internal sealed class ThermalBitmapPresenter : IDisposable
{
    private const int BytesPerPixel = 4;

    private readonly WriteableBitmap[] _bitmaps;
    private readonly int _width;
    private readonly int _height;
    private int _next;

    public ThermalBitmapPresenter(int width, int height)
    {
        _width = width;
        _height = height;
        _bitmaps = [Create(width, height), Create(width, height)];
    }

    /// <summary>Byte size of the pixel buffer <see cref="Present"/> expects.</summary>
    public int BufferSize => _width * _height * BytesPerPixel;

    private static WriteableBitmap Create(int width, int height) =>
        new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    /// <summary>Writes <paramref name="bgraPixels"/> (row-major, no padding) into the next bitmap and returns it.</summary>
    public WriteableBitmap Present(byte[] bgraPixels)
    {
        if (bgraPixels.Length < BufferSize)
        {
            throw new ArgumentException($"Expected {BufferSize} bytes, got {bgraPixels.Length}.", nameof(bgraPixels));
        }

        WriteableBitmap bitmap = _bitmaps[_next];
        _next ^= 1;

        int rowBytes = _width * BytesPerPixel;
        using (ILockedFramebuffer framebuffer = bitmap.Lock())
        {
            // The framebuffer may pad rows (RowBytes ≥ width × 4), so copy row by row.
            for (int y = 0; y < _height; y++)
            {
                Marshal.Copy(bgraPixels, y * rowBytes, framebuffer.Address + (y * framebuffer.RowBytes), rowBytes);
            }
        }

        return bitmap;
    }

    public void Dispose()
    {
        foreach (WriteableBitmap bitmap in _bitmaps)
        {
            bitmap.Dispose();
        }
    }
}
