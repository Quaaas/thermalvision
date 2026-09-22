using System.Buffers.Binary;

namespace ThermalViewer.Camera.Interop;

/// <summary>
/// An open libusb device: owns the libusb context, the device handle, the claimed
/// interfaces and a background thread that runs the libusb event loop (required for the
/// asynchronous/isochronous transfers in <see cref="IsochronousStream"/>).
///
/// Synchronous control transfers may be issued from any thread while the event loop runs;
/// libusb serializes event handling internally.
/// </summary>
internal sealed unsafe class LibUsbConnection : IDisposable
{
    private static readonly TimeSpan EventLoopJoinTimeout = TimeSpan.FromSeconds(2);

    private readonly IntPtr _context;
    private readonly IntPtr _handle;
    private readonly Thread _eventThread;
    private readonly List<int> _claimedInterfaces = [];
    private volatile bool _stopEventLoop;
    private bool _disposed;

    private LibUsbConnection(IntPtr context, IntPtr handle)
    {
        _context = context;
        _handle = handle;
        _eventThread = new Thread(RunEventLoop) { IsBackground = true, Name = "libusb event loop" };
        _eventThread.Start();
    }

    /// <summary>The native <c>libusb_device_handle*</c>.</summary>
    public IntPtr Handle => _disposed ? throw new ObjectDisposedException(nameof(LibUsbConnection)) : _handle;

    /// <summary>Last error returned by the event loop (0 = none), for diagnostics.</summary>
    public int LastEventLoopError { get; private set; }

    /// <summary>
    /// Opens the first device with the given VID/PID, or returns <c>null</c> if none is
    /// connected. Enumerates manually instead of using libusb_open_device_with_vid_pid,
    /// because that one swallows the error code (e.g. "access denied" would look like
    /// "not found").
    /// </summary>
    public static LibUsbConnection? Open(ushort vendorId, ushort productId)
    {
        LibUsbException.ThrowOnError(NativeMethods.libusb_init(out IntPtr context), "libusb_init");
        try
        {
            IntPtr handle = OpenDevice(context, vendorId, productId);
            if (handle == IntPtr.Zero)
            {
                NativeMethods.libusb_exit(context);
                return null;
            }

            return new LibUsbConnection(context, handle);
        }
        catch
        {
            NativeMethods.libusb_exit(context);
            throw;
        }
    }

    private static IntPtr OpenDevice(IntPtr context, ushort vendorId, ushort productId)
    {
        nint count = NativeMethods.libusb_get_device_list(context, out IntPtr list);
        LibUsbException.ThrowOnError((int)count, "libusb_get_device_list");
        try
        {
            var devices = (IntPtr*)list;
            byte* descriptor = stackalloc byte[NativeMethods.DeviceDescriptorLength];
            for (nint i = 0; i < count; i++)
            {
                if (NativeMethods.libusb_get_device_descriptor(devices[i], descriptor) < 0)
                {
                    continue;
                }

                var span = new ReadOnlySpan<byte>(descriptor, NativeMethods.DeviceDescriptorLength);
                ushort vid = BinaryPrimitives.ReadUInt16LittleEndian(span[NativeMethods.DeviceDescriptorVendorIdOffset..]);
                ushort pid = BinaryPrimitives.ReadUInt16LittleEndian(span[NativeMethods.DeviceDescriptorProductIdOffset..]);
                if (vid != vendorId || pid != productId)
                {
                    continue;
                }

                LibUsbException.ThrowOnError(NativeMethods.libusb_open(devices[i], out IntPtr handle), "libusb_open");
                return handle;
            }

            return IntPtr.Zero;
        }
        finally
        {
            // libusb_open took its own reference, so the list may unref all devices.
            NativeMethods.libusb_free_device_list(list, 1);
        }
    }

    /// <summary>
    /// Lets libusb detach a bound kernel driver (Linux: uvcvideo) on claim and re-attach it on
    /// release. Returns false where unsupported (Windows/macOS) — harmless there.
    /// </summary>
    public bool SetAutoDetachKernelDriver(bool enable) =>
        NativeMethods.libusb_set_auto_detach_kernel_driver(Handle, enable ? 1 : 0) == 0;

    public void ClaimInterface(int interfaceNumber)
    {
        LibUsbException.ThrowOnError(
            NativeMethods.libusb_claim_interface(Handle, interfaceNumber), $"libusb_claim_interface({interfaceNumber})");
        _claimedInterfaces.Add(interfaceNumber);
    }

    public void SetInterfaceAltSetting(int interfaceNumber, int alternateSetting) =>
        LibUsbException.ThrowOnError(
            NativeMethods.libusb_set_interface_alt_setting(Handle, interfaceNumber, alternateSetting),
            $"libusb_set_interface_alt_setting({interfaceNumber}, {alternateSetting})");

    /// <summary>
    /// Synchronous control transfer. For IN transfers <paramref name="data"/> receives the
    /// response; returns the number of bytes actually transferred.
    /// </summary>
    public int ControlTransfer(
        byte requestType, byte request, ushort value, ushort index, Span<byte> data, uint timeoutMs = 1000)
    {
        fixed (byte* buffer = data)
        {
            int result = NativeMethods.libusb_control_transfer(
                Handle, requestType, request, value, index, buffer, checked((ushort)data.Length), timeoutMs);
            return LibUsbException.ThrowOnError(
                result, $"libusb_control_transfer(0x{requestType:X2}, 0x{request:X2}, 0x{value:X4}, {index})");
        }
    }

    private void RunEventLoop()
    {
        // Short timeout so the loop notices _stopEventLoop without needing a wake-up event.
        var timeout = new TimeVal { Seconds = new CLong(0), Microseconds = new CLong(100_000) };
        while (!_stopEventLoop)
        {
            int result = NativeMethods.libusb_handle_events_timeout_completed(_context, &timeout, null);
            if (result < 0 && result != LibUsbError.Interrupted)
            {
                LastEventLoopError = result;
                Thread.Sleep(10); // don't spin if the device is gone
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Releasing the interfaces re-attaches the kernel driver (auto-detach), so the camera
        // works in other apps again after we're done. Errors are irrelevant at this point.
        foreach (int interfaceNumber in _claimedInterfaces)
        {
            NativeMethods.libusb_release_interface(_handle, interfaceNumber);
        }

        _claimedInterfaces.Clear();

        _stopEventLoop = true;
        bool eventLoopStopped = _eventThread.Join(EventLoopJoinTimeout);

        NativeMethods.libusb_close(_handle);
        if (eventLoopStopped)
        {
            // Only tear down the context once nothing can be inside libusb_handle_events anymore.
            NativeMethods.libusb_exit(_context);
        }

        _disposed = true;
    }
}
