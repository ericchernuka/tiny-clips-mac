using NAudio.Wave;

namespace TinyClips.Core.Capture;

/// <summary>
/// Places timestamped source packets on a shared recording timeline. The first packet after
/// <see cref="BeginTimeline"/> is aligned to the shared origin (leading silence is inserted, or
/// pre-origin frames are trimmed) so independent sources that start at slightly different times
/// stay in sync. Every subsequent packet is appended contiguously: re-deriving each packet's
/// position from its (jittery, frame-rounded) timestamp would insert or drop a sample or two on
/// every ~10 ms packet, producing constant audible crackle.
/// </summary>
internal sealed class TimelineAlignedWaveProvider : IWaveProvider
{
    private readonly BufferedWaveProvider _buffer;
    private TimeSpan _origin;
    private bool _timelineStarted;
    private bool _aligned;

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
        _buffer.ClearBuffer();
        _timelineStarted = true;
        _aligned = false;
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

        var byteOffset = 0;

        if (!_aligned)
        {
            _aligned = true;

            // Position only the first packet relative to the shared origin. This preserves the
            // true start offset between sources (e.g. microphone vs. system audio) without
            // re-quantizing every later packet.
            var sourceOffset = sourceTimestamp - _origin;
            var desiredStartFrame = (long)Math.Round(
                sourceOffset.Ticks * WaveFormat.SampleRate / (double)TimeSpan.TicksPerSecond);

            if (desiredStartFrame < 0)
            {
                // The first packet began before the origin: drop its pre-origin frames.
                var trimFrames = Math.Min(packetFrames, -desiredStartFrame);
                byteOffset = checked((int)(trimFrames * blockAlign));
                if (byteOffset >= count)
                {
                    return;
                }
            }
            else if (desiredStartFrame > 0)
            {
                // The source began after the origin: pad the gap with silence so it lands late.
                AddSilence(desiredStartFrame);
            }
        }

        var alignedCount = ((count - byteOffset) / blockAlign) * blockAlign;
        if (alignedCount > 0)
        {
            _buffer.AddSamples(samples, byteOffset, alignedCount);
        }
    }

    public int Read(byte[] buffer, int offset, int count) => _buffer.Read(buffer, offset, count);

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
