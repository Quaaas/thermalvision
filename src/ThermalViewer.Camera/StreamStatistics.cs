namespace ThermalViewer.Camera;

/// <summary>Snapshot of the streaming counters, for the UI and for debugging a new device.</summary>
/// <param name="FramesCompleted">Complete frames of the expected size.</param>
/// <param name="FramesDropped">Frames discarded (wrong size, ERR bit, overflow).</param>
/// <param name="LastDroppedFrameSize">Size of the last dropped frame — a constant value here usually means the expected frame size/format is wrong.</param>
/// <param name="MalformedPayloads">Packets with an invalid UVC header.</param>
/// <param name="CompletedTransfers">Isochronous transfers completed by libusb.</param>
/// <param name="TransferErrors">Transfers that failed or couldn't be resubmitted.</param>
/// <param name="PacketErrors">Individual isochronous packets with an error status.</param>
/// <param name="DeviceLost">The camera was disconnected during streaming.</param>
public sealed record StreamStatistics(
    int FramesCompleted,
    int FramesDropped,
    int LastDroppedFrameSize,
    int MalformedPayloads,
    int CompletedTransfers,
    int TransferErrors,
    int PacketErrors,
    bool DeviceLost)
{
    public override string ToString() =>
        $"{FramesCompleted} frames, {FramesDropped} dropped (last {LastDroppedFrameSize} B), " +
        $"{MalformedPayloads} malformed, {CompletedTransfers} transfers, " +
        $"{TransferErrors} transfer errors, {PacketErrors} packet errors" +
        (DeviceLost ? ", DEVICE LOST" : string.Empty);
}
