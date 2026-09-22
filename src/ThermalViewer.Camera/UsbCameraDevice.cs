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

    // bmRequestType for the vendor protocol, composed from named flags instead of magic numbers.
    private const byte VendorOutToInterface =
        (byte)(UsbCtrlFlags.Direction_Out | UsbCtrlFlags.RequestType_Vendor | UsbCtrlFlags.Recipient_Interface); // 0x41
    private const byte VendorInFromInterface =
        (byte)(UsbCtrlFlags.Direction_In | UsbCtrlFlags.RequestType_Vendor | UsbCtrlFlags.Recipient_Interface); // 0xC1

    private readonly CameraDescriptor _descriptor;
    private readonly object _commandLock = new();
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
    /// Sends a <see cref="VantrueCommand"/> and returns its response data (empty if the
    /// command has none). Follows the handshake of the manufacturer tool: write command,
    /// check status, and — only if a response is expected — read it and check status again.
    /// </summary>
    public byte[] SendCommand(VantrueCommand command)
    {
        lock (_commandLock)
        {
            WriteVendorRequest(VantrueRequest.Command, command.ToArray());
            ExpectStatus(VantrueStatus.CommandReceived, command);

            if (command.ResponseLength == 0)
            {
                return [];
            }

            byte[] response = ReadVendorRequest(VantrueRequest.Response, command.ResponseLength);
            ExpectStatus(VantrueStatus.CommandReceived | VantrueStatus.Completed, command);
            return response;
        }
    }

    public Task TriggerShutterCalibrationAsync(CancellationToken cancellationToken = default)
    {
        SendCommand(VantrueProtocol.Shutter);
        return Task.CompletedTask;
    }

    public Task<CameraInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        // The USB transfers block, so run them off the UI thread.
        return Task.Run(() => ReadDeviceInfo(cancellationToken), cancellationToken);
    }

    private CameraInfo ReadDeviceInfo(CancellationToken cancellationToken)
    {
        string model = ReadString(VantrueProtocol.ReadModel, cancellationToken);
        string firmwareVersion = ReadString(VantrueProtocol.ReadFirmwareVersion, cancellationToken);
        string hardwareVersion = ReadString(VantrueProtocol.ReadHardwareVersion, cancellationToken);
        string serialNumber = ReadString(VantrueProtocol.ReadSerialNumber, cancellationToken);

        Console.Write($"{model} {firmwareVersion} {hardwareVersion} {serialNumber}");

        return new CameraInfo(model, firmwareVersion, hardwareVersion, serialNumber);
    }

    private string ReadString(VantrueCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] response = SendCommand(command);
        return VantrueProtocol.DecodeString(response);
    }


    /// <summary>Reads the status register and checks that all <paramref name="required"/> bits are set.</summary>
    private void ExpectStatus(VantrueStatus required, VantrueCommand command)
    {
        var actual = (VantrueStatus)ReadVendorRequest(VantrueRequest.Status, 1)[0];
        if ((actual & required) != required)
        {
            throw new InvalidDataException(
                $"Unexpected status 0x{(byte)actual:X2} after command {command} " +
                $"(required bits: {required} = 0x{(byte)required:X2}).");
        }
    }

    // --- USB primitives: the only place that knows about setup packets. -------------------

    private void WriteVendorRequest(VantrueRequest request, byte[] data)
    {
        var setupPacket = new UsbSetupPacket(VendorOutToInterface, (byte)request, 0, VideoControlInterface, data.Length);
        int transferred = OpenDevice.ControlTransfer(setupPacket, data, 0, data.Length);
        EnsureComplete(request, transferred, data.Length);
    }

    private byte[] ReadVendorRequest(VantrueRequest request, int length)
    {
        var buffer = new byte[length];
        var setupPacket = new UsbSetupPacket(VendorInFromInterface, (byte)request, 0, VideoControlInterface, length);
        int transferred = OpenDevice.ControlTransfer(setupPacket, buffer, 0, length);
        EnsureComplete(request, transferred, length);
        return buffer;
    }

    private static void EnsureComplete(VantrueRequest request, int transferred, int expected)
    {
        if (transferred != expected)
        {
            throw new IOException($"Short control transfer for {request}: {transferred} of {expected} bytes.");
        }
    }

    private UsbDevice OpenDevice =>
        _device ?? throw new InvalidOperationException($"{nameof(OpenAsync)} must be called first.");

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