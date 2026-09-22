namespace ThermalViewer.Camera;

/// <summary>
/// Proprietary 18-byte command format of the Vantrue POWER TS1 (resp. its OEM chip family)
/// over USB control transfers.
///
/// Reverse-engineering reference: <see href="https://github.com/jvdillon/p3-ir-camera/issues/2"/>
/// (documented for the sibling camera P3, VID:PID 3474:45a2). NOT verified for our own
/// camera (PID 45e1, same VID/OEM family) — needs to be tested.
///
/// Byte layout:
/// <code>
/// Offset  0- 1: Command type    (LE, uint16)
/// Offset  2- 3: Parameter       (LE, uint16, usually 0x0081)
/// Offset  4- 5: Register ID     (LE, uint16)
/// Offset  6-11: Reserved        (6 bytes, 0x00)
/// Offset 12-13: Response length (LE, uint16)
/// Offset 14-15: Reserved        (2 bytes, 0x00)
/// Offset 16-17: CRC16           (LE, uint16) — per the reference, NOT checked by the device
/// </code>
/// </summary>
public static class VantrueProtocol
{
    public const int CommandLength = 18;

    /// <summary>
    /// The calibration/"click" command (shutter), as triggered by a button press in the
    /// manufacturer app. Raw bytes taken from the reference implementation for the P3
    /// sibling camera, NOT verified against our own camera (PID 45e1).
    /// </summary>
    public static readonly byte[] ShutterCommand =
        Convert.FromHexString("01364300000000000000000000000000cd0b");

    /// <summary>
    /// Builds a command from command type, parameter, register ID and expected response
    /// length. CRC16 is set to 0x0000 (per the reference the device doesn't check it) —
    /// if that turns out to be wrong for PID 45e1, plug in <see cref="ComputeCrc16"/> here.
    /// </summary>
    public static byte[] BuildCommand(ushort commandType, ushort parameter, ushort registerId, ushort responseLength)
    {
        var buffer = new byte[CommandLength];
        var span = buffer.AsSpan();

        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(span[0..2], commandType);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(span[2..4], parameter);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(span[4..6], registerId);
        // Bytes 6-11 stay reserved/0.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(span[12..14], responseLength);
        // Bytes 14-15 stay reserved/0, bytes 16-17 (CRC) stay 0 (see above).

        return buffer;
    }

    /// <summary>
    /// Placeholder for CRC16, in case our own camera does validate the checksum after all.
    /// Polynomial/variant is not known yet — implement if needed.
    /// </summary>
    public static ushort ComputeCrc16(ReadOnlySpan<byte> data) =>
        throw new NotImplementedException(
            "CRC16 variant for this device is unknown. Per the reference issue the device " +
            "doesn't check the CRC anyway — implement this only if that turns out to be wrong.");
}
