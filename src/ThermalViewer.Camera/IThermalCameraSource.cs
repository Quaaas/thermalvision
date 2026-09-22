using ThermalViewer.Core.Models;

namespace ThermalViewer.Camera;

/// <summary>
/// Abstraction over the camera source so that ViewModels/UI don't depend on LibUsbDotNet
/// directly (testable, and open to a future source such as raw data recorded to a file).
/// </summary>
public interface IThermalCameraSource : IAsyncDisposable
{
    /// <summary>Raised for every received frame, already split into image/raw-data parts.</summary>
    event EventHandler<ThermalFrame>? FrameReceived;

    /// <summary>Opens the USB device and claims the required interfaces.</summary>
    Task OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts isochronous video streaming in the given format.</summary>
    Task StartStreamingAsync(ThermalFrameFormat format, CancellationToken cancellationToken = default);

    Task StopStreamingAsync(CancellationToken cancellationToken = default);

    /// <summary>Triggers the camera's shutter/calibration function (NUC calibration, audible "click").</summary>
    Task TriggerShutterCalibrationAsync(CancellationToken cancellationToken = default);

    /// <summary> Read Camera Info</summary>
    Task<CameraInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken = default);
}
