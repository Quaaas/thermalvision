using System.Buffers.Binary;
using System.Text;
namespace ThermalViewer.Camera;

/// <summary>
/// Proprietary command protocol of the Vantrue POWER TS1 (resp. its OEM chip family)
/// over USB vendor control transfers. Everything that is protocol knowledge lives here;
/// <see cref="UsbCameraDevice"/> only knows how to move bytes over USB.
///
/// Reverse-engineering reference: <see href="https://github.com/jvdillon/p3-ir-camera/blob/main/P3_PROTOCOL.md"/>
/// (documented for the sibling camera P3, VID:PID 3474:45a2).
/// Verified on our own camera (PID 45e1): the shutter command via <see cref="VantrueRequest.Command"/>.
/// Everything else is still unverified.
///
/// Command byte layout (18 bytes):
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
    public const int ResponseLengthOffset = 12;

    public static readonly VantrueCommand ReadModel =
        VantrueCommand.FromHex("01364300000000000000000000000000cd0b");

    public static readonly VantrueCommand ReadFirmwareVersion =
        VantrueCommand.FromHex("0101810002000000000000000c0000001f63");

    public static readonly VantrueCommand ReadSerialNumber =
        VantrueCommand.FromHex("01018100070000000000000040000000104c");

    public static readonly VantrueCommand ReadHardwareVersion =
        VantrueCommand.FromHex("010181000a00000000000000400000001959");

    /// <summary>
    /// The calibration/"click" command (shutter), as triggered by a button press in the
    /// manufacturer app. Verified on PID 45e1.
    /// </summary>
    public static readonly VantrueCommand Shutter =
        VantrueCommand.FromHex("01364300000000000000000000000000cd0b");

    /// <summary>
    /// Builds a command from command type, parameter, register ID and expected response
    /// length. CRC16 is set to 0x0000 (per the reference the device doesn't check it).
    /// </summary>
    public static VantrueCommand BuildCommand(ushort commandType, ushort parameter, ushort registerId, ushort responseLength)
    {
        Span<byte> buffer = stackalloc byte[CommandLength];
        buffer.Clear();

        BinaryPrimitives.WriteUInt16LittleEndian(buffer[0..2], commandType);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[2..4], parameter);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[4..6], registerId);
        // Bytes 6-11 stay reserved/0.
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[ResponseLengthOffset..(ResponseLengthOffset + 2)], responseLength);
        // Bytes 14-15 stay reserved/0, bytes 16-17 (CRC) stay 0 (see above).

        return VantrueCommand.FromBytes(buffer);
    }


    /// <summary>Decodes a register response: ASCII, padded with NUL bytes at the end.</summary>
    public static string DecodeString(ReadOnlySpan<byte> response)
    {
        int end = response.IndexOf((byte)0);
        if (end >= 0)
        {
            response = response[..end];
        }

        return Encoding.ASCII.GetString(response).Trim();
    }

    /// <summary>
    /// Placeholder for CRC16, in case our own camera does validate the checksum after all.
    /// Per the P3 reference it is CRC16-CCITT (polynomial 0x1021, initial value 0x0000).
    /// </summary>
    public static ushort ComputeCrc16(ReadOnlySpan<byte> data) =>
        throw new NotImplementedException(
            "Per the reference the device doesn't check the CRC — implement this only if that " +
            "turns out to be wrong for PID 45e1.");
}

/// <summary>
/// The vendor-specific control requests (bRequest) of the protocol.
/// </summary>
public enum VantrueRequest : byte
{
    /// <summary>Host → device: write an 18-byte <see cref="VantrueCommand"/>.</summary>
    Command = 0x20,

    /// <summary>Device → host: read the response data of the last command.</summary>
    Response = 0x21,

    /// <summary>Device → host: read the 1-byte status/ACK register.</summary>
    Status = 0x22,
}

/// <summary>
/// Bits of the 1-byte status register (<see cref="VantrueRequest.Status"/>).
///
/// The P3 reference only documents the values 0x02 "after write" and 0x03 "after read".
/// Our PID 45e1 answers the shutter command (no response data) directly with 0x03, so the
/// register is treated as bit flags rather than fixed values. The meaning of the bits is a
/// hypothesis derived from these observations.
/// </summary>
[Flags]
public enum VantrueStatus : byte
{
    None = 0x00,

    /// <summary>Set once the device has finished processing the command (hypothesis).</summary>
    Completed = 0x01,

    /// <summary>Set once the device has received a command (hypothesis).</summary>
    CommandReceived = 0x02,
}