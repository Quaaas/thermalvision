using System.Buffers.Binary;

namespace ThermalViewer.Core.Uvc;

/// <summary>
/// The UVC "Video Probe and Commit Controls" structure (UVC 1.1, section 4.3.1.1) used to
/// negotiate format, frame size and frame interval before streaming starts. Pure data —
/// the USB requests themselves live in the camera layer.
///
/// Byte layout (little endian):
/// <code>
///  0 bmHint (2)              14 wCompWindowSize (2)          26 dwClockFrequency (4)   [UVC 1.1]
///  2 bFormatIndex (1)        16 wDelay (2)                   30 bmFramingInfo (1)      [UVC 1.1]
///  3 bFrameIndex (1)         18 dwMaxVideoFrameSize (4)      31 bPreferedVersion (1)   [UVC 1.1]
///  4 dwFrameInterval (4)     22 dwMaxPayloadTransferSize (4) 32 bMinVersion (1)        [UVC 1.1]
///  8 wKeyFrameRate (2)                                       33 bMaxVersion (1)        [UVC 1.1]
/// 10 wPFrameRate (2)
/// 12 wCompQuality (2)
/// </code>
/// </summary>
public sealed record UvcStreamingControl
{
    /// <summary>Length for UVC 1.0 devices.</summary>
    public const int Uvc10Length = 26;

    /// <summary>Length for UVC 1.1 devices (our camera reports UVC 1.10).</summary>
    public const int Uvc11Length = 34;

    /// <summary>Control selector VS_PROBE_CONTROL (goes into the high byte of wValue).</summary>
    public const byte ProbeControlSelector = 0x01;

    /// <summary>Control selector VS_COMMIT_CONTROL (goes into the high byte of wValue).</summary>
    public const byte CommitControlSelector = 0x02;

    /// <summary>bmHint bit: keep dwFrameInterval fixed during negotiation.</summary>
    public const ushort HintFrameInterval = 0x0001;

    public ushort Hint { get; init; }
    public byte FormatIndex { get; init; }
    public byte FrameIndex { get; init; }

    /// <summary>Frame interval in 100 ns units (400000 = 25 fps).</summary>
    public uint FrameInterval { get; init; }

    public ushort KeyFrameRate { get; init; }
    public ushort PFrameRate { get; init; }
    public ushort CompQuality { get; init; }
    public ushort CompWindowSize { get; init; }
    public ushort Delay { get; init; }
    public uint MaxVideoFrameSize { get; init; }
    public uint MaxPayloadTransferSize { get; init; }
    public uint ClockFrequency { get; init; }
    public byte FramingInfo { get; init; }
    public byte PreferredVersion { get; init; }
    public byte MinVersion { get; init; }
    public byte MaxVersion { get; init; }

    /// <summary>Serializes to <paramref name="length"/> bytes (26 for UVC 1.0, 34 for UVC 1.1).</summary>
    public byte[] ToBytes(int length = Uvc11Length)
    {
        if (length is not (Uvc10Length or Uvc11Length))
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, $"Must be {Uvc10Length} or {Uvc11Length}.");
        }

        var bytes = new byte[length];
        Span<byte> s = bytes;
        BinaryPrimitives.WriteUInt16LittleEndian(s[0..], Hint);
        s[2] = FormatIndex;
        s[3] = FrameIndex;
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], FrameInterval);
        BinaryPrimitives.WriteUInt16LittleEndian(s[8..], KeyFrameRate);
        BinaryPrimitives.WriteUInt16LittleEndian(s[10..], PFrameRate);
        BinaryPrimitives.WriteUInt16LittleEndian(s[12..], CompQuality);
        BinaryPrimitives.WriteUInt16LittleEndian(s[14..], CompWindowSize);
        BinaryPrimitives.WriteUInt16LittleEndian(s[16..], Delay);
        BinaryPrimitives.WriteUInt32LittleEndian(s[18..], MaxVideoFrameSize);
        BinaryPrimitives.WriteUInt32LittleEndian(s[22..], MaxPayloadTransferSize);

        if (length == Uvc11Length)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(s[26..], ClockFrequency);
            s[30] = FramingInfo;
            s[31] = PreferredVersion;
            s[32] = MinVersion;
            s[33] = MaxVersion;
        }

        return bytes;
    }

    /// <summary>Parses a GET_CUR response (at least 26 bytes; the UVC 1.1 fields are optional).</summary>
    public static UvcStreamingControl Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Uvc10Length)
        {
            throw new ArgumentException(
                $"Streaming control needs at least {Uvc10Length} bytes, got {data.Length}.", nameof(data));
        }

        bool isUvc11 = data.Length >= Uvc11Length;
        return new UvcStreamingControl
        {
            Hint = BinaryPrimitives.ReadUInt16LittleEndian(data[0..]),
            FormatIndex = data[2],
            FrameIndex = data[3],
            FrameInterval = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
            KeyFrameRate = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]),
            PFrameRate = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]),
            CompQuality = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]),
            CompWindowSize = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]),
            Delay = BinaryPrimitives.ReadUInt16LittleEndian(data[16..]),
            MaxVideoFrameSize = BinaryPrimitives.ReadUInt32LittleEndian(data[18..]),
            MaxPayloadTransferSize = BinaryPrimitives.ReadUInt32LittleEndian(data[22..]),
            ClockFrequency = isUvc11 ? BinaryPrimitives.ReadUInt32LittleEndian(data[26..]) : 0,
            FramingInfo = isUvc11 ? data[30] : (byte)0,
            PreferredVersion = isUvc11 ? data[31] : (byte)0,
            MinVersion = isUvc11 ? data[32] : (byte)0,
            MaxVersion = isUvc11 ? data[33] : (byte)0,
        };
    }

    public override string ToString() =>
        $"format {FormatIndex}, frame {FrameIndex}, interval {FrameInterval}, " +
        $"max frame {MaxVideoFrameSize} B, max payload {MaxPayloadTransferSize} B";
}
