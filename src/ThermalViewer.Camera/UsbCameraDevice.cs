using System.Threading.Channels;
using ThermalViewer.Camera.Interop;
using ThermalViewer.Core.Models;
using ThermalViewer.Core.Processing;
using ThermalViewer.Core.Uvc;

namespace ThermalViewer.Camera;

/// <summary>
/// Direct USB access to the thermal camera via libusb (own thin interop in
/// <see cref="Interop"/>): vendor control transfers for the proprietary command protocol,
/// and isochronous video streaming that bypasses the OS video stack.
///
/// Streaming follows exactly what the Linux uvcvideo driver does with this camera (which is
/// known to work — the image shows up in a standard camera app):
/// UVC probe/commit (format, frame, 25 fps) → alternate setting 1 on the streaming
/// interface → isochronous IN transfers on endpoint 0x81 → strip UVC payload headers and
/// reassemble frames (<see cref="UvcFrameAssembler"/>).
///
/// On Linux, libusb must detach uvcvideo from the camera for this (done automatically and
/// re-attached on dispose). On Windows a WinUSB driver is required (see README).
/// </summary>
public sealed class UsbCameraDevice : IThermalCameraSource
{
    private const byte VideoControlInterface = 0;
    private const byte VideoStreamingInterface = 1;
    private const byte StreamingAlternateSetting = 1;
    private const byte IsochronousEndpointAddress = 0x81;

    /// <summary>wMaxPacketSize of endpoint 0x81 in alternate setting 1 (1 × 1024 bytes per microframe).</summary>
    private const int IsochronousPacketSize = 1024;

    /// <summary>32 packets = 4 ms per transfer at high speed; 8 transfers ≈ 32 ms of buffering.</summary>
    private const int PacketsPerTransfer = 32;

    private const int TransfersInFlight = 8;

    /// <summary>
    /// bFormatIndex of the single FORMAT_UNCOMPRESSED descriptor. The two frame sizes are its
    /// frame descriptors 1 (256x194) and 2 (256x386), matching <see cref="ThermalFrameFormat"/>.
    /// Verify with <c>lsusb -v -d 3474:45e1</c> if the probe gets rejected.
    /// </summary>
    private const byte UvcFormatIndex = 1;

    /// <summary>400000 × 100 ns = 40 ms = 25 fps (the only interval the camera offers).</summary>
    private const uint FrameInterval25Fps = 400_000;

    // bmRequestType values (direction | type | recipient).
    private const byte VendorOutToInterface = 0x41; // OUT | VENDOR | INTERFACE
    private const byte VendorInFromInterface = 0xC1; // IN  | VENDOR | INTERFACE
    private const byte ClassOutToInterface = 0x21; // OUT | CLASS  | INTERFACE
    private const byte ClassInFromInterface = 0xA1; // IN  | CLASS  | INTERFACE

    // UVC class-specific requests (bRequest).
    private const byte UvcSetCur = 0x01;
    private const byte UvcGetCur = 0x81;

    private readonly CameraDescriptor _descriptor;
    private readonly object _commandLock = new();
    private readonly SemaphoreSlim _streamingLock = new(1, 1);
    private LibUsbConnection? _usb;
    private StreamingSession? _streaming;
    private bool _disposed;

    public UsbCameraDevice(CameraDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public event EventHandler<ThermalFrame>? FrameReceived;

    /// <summary>The negotiated UVC streaming parameters of the current stream (diagnostics).</summary>
    public UvcStreamingControl? NegotiatedStreamingControl => _streaming?.Negotiated;

    public Task OpenAsync(CancellationToken cancellationToken = default) =>
        Task.Run(Open, cancellationToken);

    private void Open()
    {
        if (_usb is not null)
        {
            throw new InvalidOperationException("Camera is already open.");
        }

        LibUsbConnection usb = LibUsbConnection.Open((ushort)_descriptor.VendorId, (ushort)_descriptor.ProductId)
            ?? throw new InvalidOperationException(
                $"Camera {_descriptor} not found. Is it plugged in (directly, not via a hub)?");
        try
        {
            // On Linux uvcvideo is bound to both interfaces; auto-detach unbinds it on claim
            // and re-binds it on release (see LibUsbConnection.Dispose).
            usb.SetAutoDetachKernelDriver(true);
            usb.ClaimInterface(VideoControlInterface);
            usb.ClaimInterface(VideoStreamingInterface);
        }
        catch
        {
            usb.Dispose();
            throw;
        }

        _usb = usb;
    }

    private LibUsbConnection Usb =>
        _usb ?? throw new InvalidOperationException($"{nameof(OpenAsync)} must be called first.");

    // --- Proprietary command protocol ----------------------------------------------------

    /// <summary>
    /// Sends a <see cref="VantrueCommand"/> and returns its response data (empty if the
    /// command has none). Follows the handshake of the manufacturer tool: write command,
    /// check status, and — only if a response is expected — read it and check status again.
    /// Blocking; safe to call while streaming.
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

    public Task TriggerShutterCalibrationAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => SendCommand(VantrueProtocol.Shutter), cancellationToken);

    public Task<CameraInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadDeviceInfo(cancellationToken), cancellationToken);

    private CameraInfo ReadDeviceInfo(CancellationToken cancellationToken) => new(
        Model: ReadString(VantrueProtocol.ReadModel, cancellationToken),
        FirmwareVersion: ReadString(VantrueProtocol.ReadFirmwareVersion, cancellationToken),
        HardwareVersion: ReadString(VantrueProtocol.ReadHardwareVersion, cancellationToken),
        SerialNumber: ReadString(VantrueProtocol.ReadSerialNumber, cancellationToken));

    private string ReadString(VantrueCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return VantrueProtocol.DecodeString(SendCommand(command));
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

    private void WriteVendorRequest(VantrueRequest request, byte[] data)
    {
        int transferred = Usb.ControlTransfer(VendorOutToInterface, (byte)request, 0, VideoControlInterface, data);
        EnsureComplete(request.ToString(), transferred, data.Length);
    }

    private byte[] ReadVendorRequest(VantrueRequest request, int length)
    {
        var buffer = new byte[length];
        int transferred = Usb.ControlTransfer(VendorInFromInterface, (byte)request, 0, VideoControlInterface, buffer);
        EnsureComplete(request.ToString(), transferred, length);
        return buffer;
    }

    private static void EnsureComplete(string request, int transferred, int expected)
    {
        if (transferred != expected)
        {
            throw new IOException($"Short control transfer for {request}: {transferred} of {expected} bytes.");
        }
    }

    // --- Video streaming -----------------------------------------------------------------

    public async Task StartStreamingAsync(ThermalFrameFormat format, CancellationToken cancellationToken = default)
    {
        await _streamingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_streaming is not null)
            {
                throw new InvalidOperationException("Already streaming.");
            }

            // Blocking USB setup (control transfers, a few ms) off the caller's thread.
            _streaming = await Task.Run(() => StartStreaming(format), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _streamingLock.Release();
        }
    }

    private StreamingSession StartStreaming(ThermalFrameFormat format)
    {
        LibUsbConnection usb = Usb;
        int frameSize = FrameSplitter.FrameSizeInBytes(format);

        // 1. Negotiate the format — the same probe/commit handshake uvcvideo performs.
        UvcStreamingControl negotiated = NegotiateStreamingControl(format);
        if (negotiated.MaxVideoFrameSize != 0 && negotiated.MaxVideoFrameSize != frameSize)
        {
            throw new InvalidDataException(
                $"Camera negotiated a frame size of {negotiated.MaxVideoFrameSize} bytes, expected {frameSize} " +
                $"for {format} ({negotiated}).");
        }

        // 2. Frames are reassembled on the libusb event thread and handed to a dispatcher task
        //    via a small channel, so a slow subscriber can never stall USB I/O (it only loses
        //    the oldest frames).
        var frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity: 3)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });
        var assembler = new UvcFrameAssembler(frameSize, frame => frames.Writer.TryWrite(frame));

        // 3. Selecting the alternate setting with the isochronous endpoint reserves bus
        //    bandwidth and makes the camera start sending.
        usb.SetInterfaceAltSetting(VideoStreamingInterface, StreamingAlternateSetting);

        var stream = new IsochronousStream(
            usb, IsochronousEndpointAddress, IsochronousPacketSize, PacketsPerTransfer, TransfersInFlight,
            packet => assembler.Push(packet));
        try
        {
            stream.Start();
        }
        catch
        {
            TrySetAlternateSetting(0);
            throw;
        }

        Task dispatcher = Task.Run(() => DispatchFramesAsync(format, frames.Reader));
        return new StreamingSession(negotiated, assembler, stream, frames, dispatcher);
    }

    private UvcStreamingControl NegotiateStreamingControl(ThermalFrameFormat format)
    {
        var requested = new UvcStreamingControl
        {
            Hint = UvcStreamingControl.HintFrameInterval,
            FormatIndex = UvcFormatIndex,
            FrameIndex = (byte)format,
            FrameInterval = FrameInterval25Fps,
        };

        SetStreamingControl(UvcStreamingControl.ProbeControlSelector, requested);
        UvcStreamingControl negotiated = GetStreamingControl(UvcStreamingControl.ProbeControlSelector);
        SetStreamingControl(UvcStreamingControl.CommitControlSelector, negotiated);
        return negotiated;
    }

    private void SetStreamingControl(byte controlSelector, UvcStreamingControl control)
    {
        byte[] data = control.ToBytes(UvcStreamingControl.Uvc11Length);
        int transferred = Usb.ControlTransfer(
            ClassOutToInterface, UvcSetCur, (ushort)(controlSelector << 8), VideoStreamingInterface, data);
        EnsureComplete($"SET_CUR(selector {controlSelector})", transferred, data.Length);
    }

    private UvcStreamingControl GetStreamingControl(byte controlSelector)
    {
        var buffer = new byte[UvcStreamingControl.Uvc11Length];
        int transferred = Usb.ControlTransfer(
            ClassInFromInterface, UvcGetCur, (ushort)(controlSelector << 8), VideoStreamingInterface, buffer);
        return UvcStreamingControl.Parse(buffer.AsSpan(0, transferred));
    }

    private async Task DispatchFramesAsync(ThermalFrameFormat format, ChannelReader<byte[]> frames)
    {
        await foreach (byte[] raw in frames.ReadAllAsync().ConfigureAwait(false))
        {
            ThermalFrame frame = FrameSplitter.Split(format, raw);
            try
            {
                FrameReceived?.Invoke(this, frame);
            }
#pragma warning disable CA1031 // A faulty subscriber must not end the stream.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    public StreamStatistics? GetStreamStatistics()
    {
        StreamingSession? session = _streaming;
        if (session is null)
        {
            return null;
        }

        UvcFrameAssembler a = session.Assembler;
        IsochronousStream s = session.Stream;
        return new StreamStatistics(
            a.FramesCompleted, a.FramesDropped, a.LastDroppedFrameSize, a.MalformedPayloads,
            s.CompletedTransfers, s.TransferErrors, s.PacketErrors, s.DeviceLost);
    }

    public async Task StopStreamingAsync(CancellationToken cancellationToken = default)
    {
        await _streamingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StreamingSession? session = _streaming;
            if (session is null)
            {
                return;
            }

            _streaming = null;
            await Task.Run(() =>
            {
                session.Stream.Dispose(); // cancels and waits for all transfers
                TrySetAlternateSetting(0); // alt 0 = zero bandwidth, camera stops sending
            }, CancellationToken.None).ConfigureAwait(false);

            session.Frames.Writer.TryComplete();
            await session.Dispatcher.ConfigureAwait(false);
        }
        finally
        {
            _streamingLock.Release();
        }
    }

    private void TrySetAlternateSetting(int alternateSetting)
    {
        try
        {
            _usb?.SetInterfaceAltSetting(VideoStreamingInterface, alternateSetting);
        }
        catch (LibUsbException)
        {
            // Device gone or already reset — nothing to undo.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopStreamingAsync().ConfigureAwait(false);
        _usb?.Dispose();
        _usb = null;
        _streamingLock.Dispose();
    }

    private sealed record StreamingSession(
        UvcStreamingControl Negotiated,
        UvcFrameAssembler Assembler,
        IsochronousStream Stream,
        Channel<byte[]> Frames,
        Task Dispatcher);
}
