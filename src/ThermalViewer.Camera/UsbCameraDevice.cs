using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using ThermalViewer.Core.Models;

namespace ThermalViewer.Camera;

/// <summary>
/// USB/UVC access to the thermal camera via LibUsbDotNet (libusb backend, so it works on
/// both Linux and Windows — on Windows a WinUSB driver must be installed per device via
/// Zadig, see README).
///
/// SCAFFOLD NOTE: this class is a starting point, not a finished driver. The LibUsbDotNet
/// 3.x API signatures used here (UsbSetupPacket, ControlTransfer, UsbContext.List,
/// IUsbDevice, UsbDevice.SetAutoDetachKernelDriver) have been verified against the source
/// at github.com/LibUsbDotNet/LibUsbDotNet. What's still open is isochronous streaming
/// (endpoint 0x81, alternate setting 1, 1024 bytes/packet) — only stubbed out here, since
/// it needs to be tested against the real hardware (see "Open next steps" in the project
/// status document).
/// </summary>
public sealed class UsbCameraDevice : IThermalCameraSource
{
    private const byte VideoControlInterface = 0;
    private const byte VideoStreamingInterface = 1;
    private const byte IsochronousStreamingAlternateSetting = 1;
    private const byte IsochronousEndpointAddress = 0x81;

    private readonly CameraDescriptor _descriptor;
    private UsbContext? _context;
    private UsbDevice? _device;
    private CancellationTokenSource? _streamingCts;
    private Task? _streamingTask;

    public UsbCameraDevice(CameraDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public event EventHandler<ThermalFrame>? FrameReceived;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        _context = new UsbContext();
        IUsbDevice found = _context
            .List() // UsbDeviceCollection already implements IEnumerable<IUsbDevice>
            .FirstOrDefault(d => d.VendorId == _descriptor.VendorId && d.ProductId == _descriptor.ProductId)
            ?? throw new InvalidOperationException(
                $"Camera {_descriptor} not found. Is it plugged in? On Linux you may need the " +
                "udev rule, see deploy/linux/99-thermalviewer.rules.");

        // SetAutoDetachKernelDriver only exists on the concrete UsbDevice class, not on the
        // IUsbDevice interface, hence the cast. The elements UsbContext.List() returns are
        // always UsbDevice instances under the hood.
        _device = (UsbDevice)found;

        _device.Open();

        // On Linux the kernel's uvcvideo driver auto-binds to this UVC camera (that's why it
        // already works in a normal camera app) and will hold the interface, so a plain
        // ClaimInterface fails with "device busy" even when USB permissions are fine.
        // Auto-detach must be enabled after Open() but before ClaimInterface(); it re-attaches
        // the kernel driver automatically on ReleaseInterface/Close. Not supported on Windows,
        // where no competing kernel driver claims the interface once WinUSB is installed (see
        // README), so SetAutoDetachKernelDriver simply returns false there — harmless to call.
        _device.SetAutoDetachKernelDriver(true);

        _device.ClaimInterface(VideoControlInterface);
        _device.ClaimInterface(VideoStreamingInterface);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a <see cref="VantrueProtocol"/> command as a USB control transfer and returns
    /// the response (length per <paramref name="responseLength"/>).
    /// </summary>
    public byte[] SendControlCommand(byte[] command, ushort responseLength)
    {
        if (_device is null)
        {
            throw new InvalidOperationException($"{nameof(OpenAsync)} must be called first.");
        }

        if (command.Length != VantrueProtocol.CommandLength)
        {
            throw new ArgumentException($"Command must be {VantrueProtocol.CommandLength} bytes long.", nameof(command));
        }

        // TODO: verify bmRequestType/bRequest/wValue/wIndex against a real USB capture
        // (Wireshark/usbmon) — the values below are a placeholder for a vendor control transfer.
        // UsbSetupPacket constructor: (byte bRequestType, byte bRequest, int wValue, int wIndex, int wLength) —
        // positional parameters, no named "requestType" etc. (see LibUsbDotNet.Main.UsbSetupPacket).
        var setupPacket = new UsbSetupPacket(
            0x21, // bRequestType: vendor, host-to-device, interface
            0x09, // bRequest
            0x0300, // wValue
            VideoControlInterface, // wIndex
            command.Length); // wLength

        // ControlTransfer expects the setup packet by value, not by ref.
        _device.ControlTransfer(setupPacket, command, 0, command.Length);

        var response = new byte[responseLength];
        if (responseLength > 0)
        {
            var readSetupPacket = new UsbSetupPacket(
                0xA1, // bRequestType: vendor, device-to-host, interface
                0x09, // bRequest
                0x0300, // wValue
                VideoControlInterface, // wIndex
                responseLength); // wLength
            _device.ControlTransfer(readSetupPacket, response, 0, response.Length);
        }

        return response;
    }

    public Task TriggerShutterCalibrationAsync(CancellationToken cancellationToken = default)
    {
        SendControlCommand(VantrueProtocol.ShutterCommand, responseLength: 0);
        return Task.CompletedTask;
    }

    public Task StartStreamingAsync(ThermalFrameFormat format, CancellationToken cancellationToken = default)
    {
        if (_device is null)
        {
            throw new InvalidOperationException($"{nameof(OpenAsync)} must be called first.");
        }

        _streamingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // TODO: activate alternate setting 1 on interface 1, open an isochronous reader on
        // endpoint 0x81, reassemble packets into complete frames (frame size depends on
        // `format`, see ThermalViewer.Core.Processing.FrameSplitter) and deliver them via
        // FrameReceived. Isochronous reading is the remaining hardware-facing piece of this
        // project.
        _streamingTask = Task.CompletedTask;

        throw new NotImplementedException(
            "Isochronous video streaming is not implemented yet — next step per the project " +
            "status document. FrameReceived, FrameSplitter and TemperatureDecoder are already " +
            "in place to process incoming raw frames.");
    }

    public async Task StopStreamingAsync(CancellationToken cancellationToken = default)
    {
        _streamingCts?.Cancel();
        if (_streamingTask is not null)
        {
            await _streamingTask.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopStreamingAsync().ConfigureAwait(false);
        _device?.Close();
        _context?.Dispose();
    }

    /// <summary>Helper to forward received raw frames to subscribers.</summary>
    private void OnFrameReceived(ThermalFrame frame) => FrameReceived?.Invoke(this, frame);
}
