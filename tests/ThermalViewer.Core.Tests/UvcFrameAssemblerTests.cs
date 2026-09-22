using ThermalViewer.Core.Uvc;

namespace ThermalViewer.Core.Tests;

public class UvcFrameAssemblerTests
{
    // Header as observed on the P3 sibling camera: 12 bytes, EOH|SCR|PTS (0x8c) + FID/EOF bits.
    private const int HeaderLength = 12;
    private const byte BaseInfo = 0x8C;

    private static byte[] Payload(int frameId, bool endOfFrame, ReadOnlySpan<byte> data, bool error = false)
    {
        var payload = new byte[HeaderLength + data.Length];
        payload[0] = HeaderLength;
        payload[1] = (byte)(BaseInfo | frameId | (endOfFrame ? 0x02 : 0) | (error ? 0x40 : 0));
        data.CopyTo(payload.AsSpan(HeaderLength));
        return payload;
    }

    private static (UvcFrameAssembler Assembler, List<byte[]> Frames) Create(int frameSize)
    {
        var frames = new List<byte[]>();
        return (new UvcFrameAssembler(frameSize, frames.Add), frames);
    }

    [Fact]
    public void Push_FrameSplitAcrossPayloadsWithEof_EmitsReassembledFrameWithoutHeaders()
    {
        var (assembler, frames) = Create(frameSize: 6);

        assembler.Push(Payload(0, false, [1, 2]));
        assembler.Push(Payload(0, false, [3, 4]));
        assembler.Push(Payload(0, true, [5, 6]));

        byte[] frame = Assert.Single(frames);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, frame);
        Assert.Equal(1, assembler.FramesCompleted);
        Assert.Equal(0, assembler.FramesDropped);
    }

    [Fact]
    public void Push_FrameIdToggleWithoutEof_ClosesPreviousFrame()
    {
        var (assembler, frames) = Create(frameSize: 4);

        assembler.Push(Payload(0, false, [1, 2, 3, 4]));
        assembler.Push(Payload(1, false, [5, 6]));

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, Assert.Single(frames));
    }

    [Fact]
    public void Push_HeaderOnlyPayloadsAfterEofWithSameFid_AreIgnored()
    {
        var (assembler, frames) = Create(frameSize: 2);

        assembler.Push(Payload(0, true, [1, 2]));
        assembler.Push(Payload(0, false, []));
        assembler.Push(Payload(0, true, []));
        assembler.Push(Payload(1, true, [3, 4]));

        Assert.Equal(2, frames.Count);
        Assert.Equal(new byte[] { 3, 4 }, frames[1]);
        Assert.Equal(0, assembler.FramesDropped);
    }

    [Fact]
    public void Push_PartialFirstFrame_IsDroppedAndNextFrameIsEmitted()
    {
        var (assembler, frames) = Create(frameSize: 4);

        // Joined mid-stream: only the tail of frame 0 arrives.
        assembler.Push(Payload(0, true, [9, 9]));
        assembler.Push(Payload(1, false, [1, 2]));
        assembler.Push(Payload(1, true, [3, 4]));

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, Assert.Single(frames));
        Assert.Equal(1, assembler.FramesDropped);
        Assert.Equal(2, assembler.LastDroppedFrameSize);
    }

    [Fact]
    public void Push_ErrorBitSet_DropsFrame()
    {
        var (assembler, frames) = Create(frameSize: 2);

        assembler.Push(Payload(0, false, [1], error: true));
        assembler.Push(Payload(0, true, [2]));

        Assert.Empty(frames);
        Assert.Equal(1, assembler.FramesDropped);
    }

    [Fact]
    public void Push_FrameLargerThanExpected_DropsFrameAndRecordsSize()
    {
        var (assembler, frames) = Create(frameSize: 2);

        assembler.Push(Payload(0, false, [1, 2]));
        assembler.Push(Payload(0, true, [3]));

        Assert.Empty(frames);
        Assert.Equal(3, assembler.LastDroppedFrameSize);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x0C })] // shorter than 2 bytes
    [InlineData(new byte[] { 0x01, 0x8C })] // header length < 2
    [InlineData(new byte[] { 0x0C, 0x8C, 0x00 })] // header length > payload
    public void Push_MalformedHeader_IsCountedAndIgnored(byte[] payload)
    {
        var (assembler, frames) = Create(frameSize: 2);

        assembler.Push(payload);

        Assert.Empty(frames);
        Assert.Equal(1, assembler.MalformedPayloads);
    }

    [Fact]
    public void Push_EmittedFrames_AreIndependentCopies()
    {
        var (assembler, frames) = Create(frameSize: 1);

        assembler.Push(Payload(0, true, [1]));
        assembler.Push(Payload(1, true, [2]));

        Assert.Equal(1, frames[0][0]);
        Assert.Equal(2, frames[1][0]);
    }
}
