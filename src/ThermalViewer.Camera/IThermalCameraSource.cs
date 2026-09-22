using ThermalViewer.Core.Models;

namespace ThermalViewer.Camera;

/// <summary>
/// Abstraction over the camera source so that ViewModels/UI don't depend on libusb
/// directly (testable, and open to a future source such as raw data recorded to a file).
/// </summary>
public interface IThermalCameraSource : IAsyncDisposable
{
    /// <summary>
    /// Raised for every received frame, already split into image/raw-data parts.
    /// Raised on a background thread — marshal to the UI thread before touching UI state.
    /// </summary>
    event EventHandler<ThermalFrame>? FrameReceived;

    /// <summary>Opens the USB device and claims the required interfaces.</summary>
    Task OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts isochronous video streaming in the given format.</summary>
    Task StartStreamingAsync(ThermalFrameFormat format, CancellationToken cancellationToken = default);

    Task StopStreamingAsync(CancellationToken cancellationToken = default);

    /// <summary>Current streaming counters, or <c>null</c> if not streaming.</summary>
    StreamStatistics? GetStreamStatistics();

    /// <summary>Triggers the camera's shutter/calibration function (NUC calibration, audible "click").</summary>
    Task TriggerShutterCalibrationAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads model, firmware/hardware version and serial number.</summary>
    Task<CameraInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken = default);
}
