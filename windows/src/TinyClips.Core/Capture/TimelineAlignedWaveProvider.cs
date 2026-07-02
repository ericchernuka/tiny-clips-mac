using NAudio.Wave;

namespace TinyClips.Core.Capture;

/// <summary>
/// Places timestamped source packets on a shared recording timeline. Reads before a
/// source begins produce silence, while late/overlapping packets are trimmed rather
/// than shifting everything that follows.
/// </summary>
internal sealed class TimelineAlignedWaveProvider : IWaveProvider
{
    private readonly BufferedWaveProvider _buffer;
    private TimeSpan _origin;
    private long _framesRead;
    private bool _timelineStarted;

    public TimelineAlignedWaveProvider(WaveFormat waveFormat)
    {
        WaveFormat = waveFormat;
        _buffer = new BufferedWaveProvider(waveFormat)
        {
            ReadFully = true,
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5),
        };
    }

    public WaveFormat WaveFormat { get; }

    public void BeginTimeline(TimeSpan origin)
    {
        _origin = origin;
        _framesRead = 0;
        _buffer.ClearBuffer();
        _timelineStarted = true;
    }

    public void AddSamples(byte[] samples, int count, TimeSpan sourceTimestamp)
    {
        if (!_timelineStarted || count <= 0)
        {
            return;
        }

        var blockAlign = WaveFormat.BlockAlign;
        var packetFrames = count / blockAlign;
        if (packetFrames <= 0)
        {
            return;
        }

        var sourceOffset = sourceTimestamp - _origin;
        var desiredStartFrame = (long)Math.Round(
            sourceOffset.Ticks * WaveFormat.SampleRate / (double)TimeSpan.TicksPerSecond);
        var sourceFrameOffset = 0L;

        if (desiredStartFrame < 0)
        {
            sourceFrameOffset = Math.Min(packetFrames, -desiredStartFrame);
            desiredStartFrame += sourceFrameOffset;
        }

        var writeCursor = _framesRead + (_buffer.BufferedBytes / blockAlign);
        if (desiredStartFrame < writeCursor)
        {
            var overlap = Math.Min(packetFrames - sourceFrameOffset, writeCursor - desiredStartFrame);
            sourceFrameOffset += overlap;
            desiredStartFrame += overlap;
        }

        if (sourceFrameOffset >= packetFrames)
        {
            return;
        }

        if (desiredStartFrame > writeCursor)
        {
            AddSilence(desiredStartFrame - writeCursor);
        }

        var byteOffset = checked((int)(sourceFrameOffset * blockAlign));
        var alignedCount = ((count - byteOffset) / blockAlign) * blockAlign;
        if (alignedCount > 0)
        {
            _buffer.AddSamples(samples, byteOffset, alignedCount);
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var read = _buffer.Read(buffer, offset, count);
        _framesRead += read / WaveFormat.BlockAlign;
        return read;
    }

    private void AddSilence(long frameCount)
    {
        const int MaxChunkBytes = 16 * 1024;
        var blockAlign = WaveFormat.BlockAlign;
        var framesPerChunk = Math.Max(1, MaxChunkBytes / blockAlign);
        var silence = new byte[framesPerChunk * blockAlign];

        while (frameCount > 0)
        {
            var frames = (int)Math.Min(frameCount, framesPerChunk);
            _buffer.AddSamples(silence, 0, frames * blockAlign);
            frameCount -= frames;
        }
    }
}
