using ThermalViewer.Core.Uvc;

namespace ThermalViewer.Core.Tests;

public class UvcStreamingControlTests
{
    [Fact]
    public void ToBytes_ProbeRequest_HasUvc11LayoutWithLittleEndianFields()
    {
        var control = new UvcStreamingControl
        {
            Hint = UvcStreamingControl.HintFrameInterval,
            FormatIndex = 1,
            FrameIndex = 2,
            FrameInterval = 400_000, // 0x00061A80
        };

        byte[] bytes = control.ToBytes();

        Assert.Equal(UvcStreamingControl.Uvc11Length, bytes.Length);
        Assert.Equal(new byte[] { 0x01, 0x00, 0x01, 0x02, 0x80, 0x1A, 0x06, 0x00 }, bytes[..8]);
        Assert.All(bytes[8..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Parse_RoundTripsAllFields()
    {
        var original = new UvcStreamingControl
        {
            Hint = 1,
            FormatIndex = 1,
            FrameIndex = 2,
            FrameInterval = 400_000,
            KeyFrameRate = 3,
            PFrameRate = 4,
            CompQuality = 5,
            CompWindowSize = 6,
            Delay = 7,
            MaxVideoFrameSize = 197_632,
            MaxPayloadTransferSize = 1024,
            ClockFrequency = 48_000_000,
            FramingInfo = 3,
            PreferredVersion = 1,
            MinVersion = 1,
            MaxVersion = 1,
        };

        Assert.Equal(original, UvcStreamingControl.Parse(original.ToBytes()));
    }

    [Fact]
    public void Parse_Uvc10Response_LeavesUvc11FieldsZero()
    {
        var original = new UvcStreamingControl { FormatIndex = 1, FrameIndex = 1, MaxVideoFrameSize = 99_328 };

        UvcStreamingControl parsed = UvcStreamingControl.Parse(original.ToBytes(UvcStreamingControl.Uvc10Length));

        Assert.Equal(99_328u, parsed.MaxVideoFrameSize);
        Assert.Equal(0u, parsed.ClockFrequency);
    }

    [Fact]
    public void Parse_TooShort_Throws()
    {
        Assert.Throws<ArgumentException>(() => UvcStreamingControl.Parse(new byte[25]));
    }
}
