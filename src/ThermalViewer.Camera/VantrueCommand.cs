using System.Buffers.Binary;

namespace ThermalViewer.Camera;

/// <summary>
/// One 18-byte command of the <see cref="VantrueProtocol"/>. Validated once on construction,
/// so everything that sends a command can rely on its length being correct.
/// </summary>
public sealed class VantrueCommand
{
    private readonly byte[] _bytes;

    private VantrueCommand(byte[] bytes)
    {
        if (bytes.Length != VantrueProtocol.CommandLength)
        {
            throw new ArgumentException(
                $"A command must be {VantrueProtocol.CommandLength} bytes long, got {bytes.Length}.",
                nameof(bytes));
        }

        _bytes = bytes;
    }

    /// <summary>
    /// Number of response bytes the device will deliver for this command, as encoded in the
    /// command itself (offset 12-13, LE). 0 means the command has no response data.
    /// </summary>
    public int ResponseLength =>
        BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(VantrueProtocol.ResponseLengthOffset, 2));

    /// <summary>Creates a command from its raw bytes (copied, so the caller can't mutate it afterwards).</summary>
    public static VantrueCommand FromBytes(ReadOnlySpan<byte> bytes) => new(bytes.ToArray());

    /// <summary>Creates a command from a hex string, e.g. as captured with Wireshark/usbmon.</summary>
    public static VantrueCommand FromHex(string hex) => new(Convert.FromHexString(hex));

    /// <summary>Returns a copy of the raw bytes to put on the wire.</summary>
    public byte[] ToArray() => (byte[])_bytes.Clone();

    public override string ToString() => Convert.ToHexString(_bytes);
}