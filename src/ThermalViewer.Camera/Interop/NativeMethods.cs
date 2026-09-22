using System.Reflection;
using System.Runtime.InteropServices;

namespace ThermalViewer.Camera.Interop;

/// <summary>
/// Minimal P/Invoke surface for libusb-1.0 — exactly the functions this project needs,
/// including the asynchronous transfer API for isochronous streaming (which LibUsbDotNet 3.x
/// does not support: its endpoint readers throw "Isochronous not supported yet").
///
/// Signatures follow <c>libusb.h</c> 1:1; names are kept identical to the C API on purpose,
/// so they can be looked up directly in the libusb documentation.
/// </summary>
internal static unsafe class NativeMethods
{
    private const string Library = "libusb-1.0";

    static NativeMethods()
    {
        // Distributions without the -dev package only ship the versioned SONAME
        // (libusb-1.0.so.0), which the default .NET probing doesn't try.
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveLibUsb);
    }

    private static IntPtr ResolveLibUsb(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Library)
        {
            return IntPtr.Zero;
        }

        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out IntPtr handle))
        {
            return handle;
        }

        if (OperatingSystem.IsLinux() && NativeLibrary.TryLoad("libusb-1.0.so.0", assembly, searchPath, out handle))
        {
            return handle;
        }

        return IntPtr.Zero; // fall back to the runtime's default error ("Unable to load ...")
    }

    // --- Library / context --------------------------------------------------------------

    [DllImport(Library)]
    public static extern int libusb_init(out IntPtr context);

    [DllImport(Library)]
    public static extern void libusb_exit(IntPtr context);

    [DllImport(Library)]
    public static extern IntPtr libusb_error_name(int errorCode);

    /// <remarks><c>tv</c> is a <c>struct timeval*</c>, <c>completed</c> may be null.</remarks>
    [DllImport(Library)]
    public static extern int libusb_handle_events_timeout_completed(IntPtr context, TimeVal* tv, int* completed);

    // --- Device enumeration / handles ---------------------------------------------------

    /// <returns>Number of devices (ssize_t) or a negative error code.</returns>
    [DllImport(Library)]
    public static extern nint libusb_get_device_list(IntPtr context, out IntPtr list);

    [DllImport(Library)]
    public static extern void libusb_free_device_list(IntPtr list, int unrefDevices);

    /// <param name="descriptor">Buffer of at least <see cref="DeviceDescriptorLength"/> bytes.</param>
    [DllImport(Library)]
    public static extern int libusb_get_device_descriptor(IntPtr device, byte* descriptor);

    [DllImport(Library)]
    public static extern int libusb_open(IntPtr device, out IntPtr handle);

    [DllImport(Library)]
    public static extern void libusb_close(IntPtr handle);

    [DllImport(Library)]
    public static extern int libusb_set_auto_detach_kernel_driver(IntPtr handle, int enable);

    [DllImport(Library)]
    public static extern int libusb_claim_interface(IntPtr handle, int interfaceNumber);

    [DllImport(Library)]
    public static extern int libusb_release_interface(IntPtr handle, int interfaceNumber);

    [DllImport(Library)]
    public static extern int libusb_set_interface_alt_setting(IntPtr handle, int interfaceNumber, int alternateSetting);

    // --- Synchronous control transfers --------------------------------------------------

    /// <returns>Number of bytes transferred, or a negative error code.</returns>
    [DllImport(Library)]
    public static extern int libusb_control_transfer(
        IntPtr handle, byte bmRequestType, byte bRequest, ushort wValue, ushort wIndex,
        byte* data, ushort wLength, uint timeoutMs);

    // --- Asynchronous transfers (used for isochronous streaming) ------------------------

    [DllImport(Library)]
    public static extern LibUsbTransfer* libusb_alloc_transfer(int isoPackets);

    [DllImport(Library)]
    public static extern void libusb_free_transfer(LibUsbTransfer* transfer);

    [DllImport(Library)]
    public static extern int libusb_submit_transfer(LibUsbTransfer* transfer);

    [DllImport(Library)]
    public static extern int libusb_cancel_transfer(LibUsbTransfer* transfer);

    // --- Constants ------------------------------------------------------------------------

    /// <summary>sizeof(struct libusb_device_descriptor).</summary>
    public const int DeviceDescriptorLength = 18;

    public const int DeviceDescriptorVendorIdOffset = 8;
    public const int DeviceDescriptorProductIdOffset = 10;

    public const byte TransferTypeIsochronous = 1;

    /// <summary>
    /// Pointer to the <c>iso_packet_desc[]</c> flexible array member that directly follows
    /// <c>num_iso_packets</c>. Note: this is NOT <c>sizeof(libusb_transfer)</c> — on 64-bit
    /// the struct is padded to 64 bytes, but the array starts at offset 60.
    /// </summary>
    public static LibUsbIsoPacketDescriptor* IsoPackets(LibUsbTransfer* transfer) =>
        (LibUsbIsoPacketDescriptor*)((byte*)&transfer->NumIsoPackets + sizeof(int));

    public static string ErrorName(int errorCode) =>
        Marshal.PtrToStringAnsi(libusb_error_name(errorCode)) ?? $"LIBUSB_ERROR({errorCode})";
}

/// <summary><c>struct timeval</c> — <c>long</c> fields, i.e. 32 bit on Windows, 64 bit on Linux x64.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TimeVal
{
    public CLong Seconds;
    public CLong Microseconds;
}

/// <summary>
/// <c>struct libusb_transfer</c> without its trailing flexible array member
/// (use <see cref="NativeMethods.IsoPackets"/> to access it). Only ever used via pointers
/// returned by <see cref="NativeMethods.libusb_alloc_transfer"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LibUsbTransfer
{
    public IntPtr DevHandle;
    public byte Flags;
    public byte Endpoint;
    public byte Type;
    public uint Timeout;
    public LibUsbTransferStatus Status;
    public int Length;
    public int ActualLength;
    public IntPtr Callback;
    public IntPtr UserData;
    public byte* Buffer;
    public int NumIsoPackets;
}

/// <summary><c>struct libusb_iso_packet_descriptor</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LibUsbIsoPacketDescriptor
{
    public uint Length;
    public uint ActualLength;
    public LibUsbTransferStatus Status;
}

/// <summary><c>enum libusb_transfer_status</c>.</summary>
internal enum LibUsbTransferStatus
{
    Completed = 0,
    Error = 1,
    TimedOut = 2,
    Cancelled = 3,
    Stall = 4,
    NoDevice = 5,
    Overflow = 6,
}

/// <summary>The subset of <c>enum libusb_error</c> this project reacts to explicitly.</summary>
internal static class LibUsbError
{
    public const int Access = -3;
    public const int NoDevice = -4;
    public const int NotFound = -5;
    public const int Busy = -6;
    public const int Interrupted = -10;
    public const int NotSupported = -12;
}
