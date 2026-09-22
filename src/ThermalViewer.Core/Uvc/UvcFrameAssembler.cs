namespace ThermalViewer.Core.Uvc;

/// <summary>Bits of the UVC payload header's bmHeaderInfo byte (UVC 1.1, section 2.4.3.3).</summary>
[Flags]
public enum UvcHeaderInfo : byte
{
    None = 0x00,

    /// <summary>FID — toggles with every new video frame.</summary>
    FrameId = 0x01,

    /// <summary>EOF — this payload is the last one of the current frame.</summary>
    EndOfFrame = 0x02,

    /// <summary>PTS — header contains a 4-byte presentation time stamp.</summary>
    PresentationTime = 0x04,

    /// <summary>SCR — header contains a 6-byte source clock reference.</summary>
    SourceClockReference = 0x08,

    StillImage = 0x20,

    /// <summary>ERR — the device signals an error in this payload.</summary>
    Error = 0x40,

    /// <summary>EOH — end of header.</summary>
    EndOfHeader = 0x80,
}

/// <summary>
/// Reassembles complete video frames from UVC payloads (one isochronous packet = one
/// payload = header + data), the same way the uvcvideo kernel driver does:
/// strip the header, append the data, and close the frame on EOF or when FID toggles.
///
/// Only frames with exactly the expected size and without the ERR bit are emitted; all
/// others (e.g. the partial first frame after joining a running stream, or the odd frame
/// after a manual shutter) are counted as dropped. Pure logic, no USB dependency — fed by
/// the camera layer, testable with synthetic payloads.
///
/// Example: the 12-byte "start/end markers" described in the P3 reverse-engineering notes
/// (length 0x0c, sync 0x8c/0x8d/0x8e/0x8f) are exactly such headers:
/// EOH|SCR|PTS (+FID) (+EOF).
/// </summary>
public sealed class UvcFrameAssembler
{
    private const int MinimumHeaderLength = 2;

    private readonly byte[] _buffer;
    private readonly Action<byte[]> _onFrame;
    private int _length;
    private int _currentFrameId = -1;
    private int _finishedFrameId = -1;
    private bool _currentFrameIsBad;

    /// <param name="frameSize">Exact size of a complete frame in bytes.</param>
    /// <param name="onFrame">Receives a fresh copy of every complete frame.</param>
    public UvcFrameAssembler(int frameSize, Action<byte[]> onFrame)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameSize);
        _buffer = new byte[frameSize];
        _onFrame = onFrame;
    }

    public int FrameSize => _buffer.Length;
    public int FramesCompleted { get; private set; }
    public int FramesDropped { get; private set; }
    public int MalformedPayloads { get; private set; }

    /// <summary>Size of the most recently dropped frame (diagnostics: wrong format/layout?).</summary>
    public int LastDroppedFrameSize { get; private set; }

    /// <summary>Processes one payload (header + data).</summary>
    public void Push(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < MinimumHeaderLength)
        {
            MalformedPayloads++;
            return;
        }

        int headerLength = payload[0];
        if (headerLength < MinimumHeaderLength || headerLength > payload.Length)
        {
            MalformedPayloads++;
            return;
        }

        var info = (UvcHeaderInfo)payload[1];
        int frameId = (info & UvcHeaderInfo.FrameId) != 0 ? 1 : 0;

        // Payloads that still carry the FID of a frame we already closed via EOF
        // (typically header-only packets) don't belong to the next frame.
        if (frameId == _finishedFrameId)
        {
            return;
        }

        _finishedFrameId = -1;

        if (_currentFrameId != -1 && frameId != _currentFrameId)
        {
            // FID toggled without a preceding EOF: the previous frame ends here.
            FinishFrame();
        }

        _currentFrameId = frameId;

        if ((info & UvcHeaderInfo.Error) != 0)
        {
            _currentFrameIsBad = true;
        }

        ReadOnlySpan<byte> data = payload[headerLength..];
        if (_length + data.Length > _buffer.Length)
        {
            _currentFrameIsBad = true; // overflow: keep counting the size, drop the frame
        }
        else
        {
            data.CopyTo(_buffer.AsSpan(_length));
        }

        _length += data.Length;

        if ((info & UvcHeaderInfo.EndOfFrame) != 0)
        {
            FinishFrame();
            _finishedFrameId = frameId;
        }
    }

    private void FinishFrame()
    {
        if (_length == _buffer.Length && !_currentFrameIsBad)
        {
            FramesCompleted++;
            _onFrame(_buffer.AsSpan().ToArray());
        }
        else if (_length > 0 || _currentFrameIsBad)
        {
            FramesDropped++;
            LastDroppedFrameSize = _length;
        }

        _length = 0;
        _currentFrameIsBad = false;
        _currentFrameId = -1;
    }
}
