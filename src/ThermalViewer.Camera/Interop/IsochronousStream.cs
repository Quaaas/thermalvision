using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ThermalViewer.Camera.Interop;

/// <summary>Receives the payload of one isochronous packet (only valid during the call).</summary>
internal delegate void IsochronousPacketHandler(ReadOnlySpan<byte> packet);

/// <summary>
/// Keeps a ring of isochronous IN transfers in flight on one endpoint and hands every
/// received packet to a <see cref="IsochronousPacketHandler"/>. This is the userspace
/// equivalent of the URB ring the uvcvideo kernel driver uses.
///
/// Threading: the packet handler and all statistics updates run on the libusb event-loop
/// thread of the <see cref="LibUsbConnection"/> — keep the handler fast (copy and return).
/// </summary>
internal sealed unsafe class IsochronousStream : IDisposable
{
    private static readonly TimeSpan CancelTimeout = TimeSpan.FromSeconds(2);

    private readonly LibUsbConnection _connection;
    private readonly byte _endpoint;
    private readonly int _packetSize;
    private readonly int _packetsPerTransfer;
    private readonly IsochronousPacketHandler _onPacket;
    private readonly LibUsbTransfer*[] _transfers;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _allTransfersReturned = new(initialState: true);
    private GCHandle _self;
    private int _inFlight;
    private bool _stopping;
    private bool _disposed;

    /// <param name="packetSize">
    /// Bytes per isochronous packet = wMaxPacketSize of the endpoint in the active alternate
    /// setting (including the high-bandwidth multiplier, if any).
    /// </param>
    /// <param name="packetsPerTransfer">Packets per transfer; 32 ≙ 4 ms at high speed.</param>
    /// <param name="transferCount">Number of transfers kept in flight at the same time.</param>
    public IsochronousStream(
        LibUsbConnection connection, byte endpoint, int packetSize, int packetsPerTransfer, int transferCount,
        IsochronousPacketHandler onPacket)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(packetSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(packetsPerTransfer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(transferCount);

        _connection = connection;
        _endpoint = endpoint;
        _packetSize = packetSize;
        _packetsPerTransfer = packetsPerTransfer;
        _onPacket = onPacket;
        _transfers = new LibUsbTransfer*[transferCount];
    }

    public int CompletedTransfers { get; private set; }
    public int TransferErrors { get; private set; }
    public int PacketErrors { get; private set; }
    public bool DeviceLost { get; private set; }

    /// <summary>First exception thrown by the packet handler, if any (it can't propagate through native code).</summary>
    public Exception? HandlerException { get; private set; }

    /// <summary>Allocates all transfers and submits them.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_self.IsAllocated)
        {
            throw new InvalidOperationException("Stream was already started.");
        }

        _self = GCHandle.Alloc(this);
        try
        {
            for (int i = 0; i < _transfers.Length; i++)
            {
                _transfers[i] = AllocateTransfer();
            }

            lock (_gate)
            {
                for (int i = 0; i < _transfers.Length; i++)
                {
                    Submit(_transfers[i]);
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private LibUsbTransfer* AllocateTransfer()
    {
        LibUsbTransfer* transfer = NativeMethods.libusb_alloc_transfer(_packetsPerTransfer);
        if (transfer == null)
        {
            throw new OutOfMemoryException("libusb_alloc_transfer failed.");
        }

        int length = _packetSize * _packetsPerTransfer;
        delegate* unmanaged[Cdecl]<LibUsbTransfer*, void> callback = &OnTransferCompleted;

        // Equivalent of the inline helpers libusb_fill_iso_transfer + libusb_set_iso_packet_lengths.
        transfer->DevHandle = _connection.Handle;
        transfer->Endpoint = _endpoint;
        transfer->Type = NativeMethods.TransferTypeIsochronous;
        transfer->Timeout = 0;
        transfer->Buffer = (byte*)NativeMemory.Alloc((nuint)length);
        transfer->Length = length;
        transfer->NumIsoPackets = _packetsPerTransfer;
        transfer->Callback = (IntPtr)(void*)callback;
        transfer->UserData = GCHandle.ToIntPtr(_self);

        LibUsbIsoPacketDescriptor* packets = NativeMethods.IsoPackets(transfer);
        for (int i = 0; i < _packetsPerTransfer; i++)
        {
            packets[i].Length = (uint)_packetSize;
        }

        return transfer;
    }

    /// <remarks>Must be called with <see cref="_gate"/> held.</remarks>
    private void Submit(LibUsbTransfer* transfer)
    {
        if (_inFlight++ == 0)
        {
            _allTransfersReturned.Reset();
        }

        int result = NativeMethods.libusb_submit_transfer(transfer);
        if (result < 0)
        {
            TransferReturned();
            throw new LibUsbException("libusb_submit_transfer", result);
        }
    }

    /// <remarks>Must be called with <see cref="_gate"/> held.</remarks>
    private void TransferReturned()
    {
        if (--_inFlight == 0)
        {
            _allTransfersReturned.Set();
        }
    }

    // libusb declares the callback LIBUSB_CALL (= WINAPI on Windows). On 64-bit platforms
    // there is only one calling convention, so Cdecl is correct everywhere we target.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnTransferCompleted(LibUsbTransfer* transfer)
    {
        // Exceptions must never escape into native code (that would terminate the process).
        try
        {
            var stream = (IsochronousStream?)GCHandle.FromIntPtr(transfer->UserData).Target;
            stream?.HandleCompletedTransfer(transfer);
        }
        catch
        {
            // Nothing sensible left to do here; HandleCompletedTransfer records its own errors.
        }
    }

    private void HandleCompletedTransfer(LibUsbTransfer* transfer)
    {
        switch (transfer->Status)
        {
            case LibUsbTransferStatus.Completed:
                CompletedTransfers++;
                DeliverPackets(transfer);
                break;
            case LibUsbTransferStatus.Cancelled:
                break;
            case LibUsbTransferStatus.NoDevice:
                DeviceLost = true;
                break;
            default:
                TransferErrors++;
                break;
        }

        lock (_gate)
        {
            if (!_stopping && !DeviceLost)
            {
                int result = NativeMethods.libusb_submit_transfer(transfer);
                if (result == 0)
                {
                    return; // still in flight
                }

                TransferErrors++;
                DeviceLost |= result == LibUsbError.NoDevice;
            }

            TransferReturned();
        }
    }

    private void DeliverPackets(LibUsbTransfer* transfer)
    {
        LibUsbIsoPacketDescriptor* packets = NativeMethods.IsoPackets(transfer);
        for (int i = 0; i < transfer->NumIsoPackets; i++)
        {
            LibUsbIsoPacketDescriptor packet = packets[i];
            if (packet.Status != LibUsbTransferStatus.Completed)
            {
                PacketErrors++;
                continue;
            }

            if (packet.ActualLength == 0)
            {
                continue;
            }

            // All packets have the same nominal length, so packet i starts at i * packetSize
            // (libusb_get_iso_packet_buffer_simple).
            var data = new ReadOnlySpan<byte>(transfer->Buffer + (i * _packetSize), (int)packet.ActualLength);
            try
            {
                _onPacket(data);
            }
            catch (Exception ex)
            {
                HandlerException ??= ex;
            }
        }
    }

    /// <summary>Cancels all transfers and waits until libusb has handed every one of them back.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            for (int i = 0; i < _transfers.Length; i++)
            {
                LibUsbTransfer* transfer = _transfers[i];
                if (transfer != null)
                {
                    // LIBUSB_ERROR_NOT_FOUND = not in flight anymore; fine.
                    NativeMethods.libusb_cancel_transfer(transfer);
                }
            }
        }

        // The cancellations are delivered via the event loop thread.
        _allTransfersReturned.Wait(CancelTimeout);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();

        bool safeToFree;
        lock (_gate)
        {
            safeToFree = _inFlight == 0;
        }

        // If libusb still owns a transfer (cancel timed out), leaking it is the lesser evil
        // compared to a use-after-free from the event loop thread.
        if (safeToFree)
        {
            for (int i = 0; i < _transfers.Length; i++)
            {
                LibUsbTransfer* transfer = _transfers[i];
                if (transfer == null)
                {
                    continue;
                }

                NativeMemory.Free(transfer->Buffer);
                NativeMethods.libusb_free_transfer(transfer);
                _transfers[i] = null;
            }

            if (_self.IsAllocated)
            {
                _self.Free();
            }
        }

        _allTransfersReturned.Dispose();
        _disposed = true;
    }
}
