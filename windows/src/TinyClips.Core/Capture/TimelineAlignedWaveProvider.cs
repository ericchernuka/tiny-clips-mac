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
    private TimeSpan _latency;
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

    /// <summary>How much captured, timeline-aligned audio is currently buffered and ready to read.</summary>
    public TimeSpan BufferedDuration => _timelineStarted && _aligned ? _buffer.BufferedDuration : TimeSpan.Zero;

    /// <summary>
    /// The source's capture latency. Audio is advanced by this amount during alignment so the
    /// recorded sound lines up with video captured at the same wall-clock instant.
    /// </summary>
    public TimeSpan Latency
    {
        get => _latency;
        set => _latency = value;
    }

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
            // Position the stream relative to the shared origin, compensating for the source's
            // input latency so captured sound lands at the wall-clock moment it actually occurred
            // (WASAPI timestamps the buffer read, which trails the real acoustic capture time).
            var sourceOffset = sourceTimestamp - _origin - _latency;
            var desiredStartFrame = (long)Math.Round(
                sourceOffset.Ticks * WaveFormat.SampleRate / (double)TimeSpan.TicksPerSecond);

            if (desiredStartFrame + packetFrames <= 0)
            {
                // The entire packet is before the (latency-compensated) origin. Drop it and keep
                // waiting: later packets carry later timestamps, so one of them will straddle the
                // origin. This discards ALL pre-origin pre-roll, not just the first packet's worth.
                return;
            }

            _aligned = true;

            if (desiredStartFrame < 0)
            {
                // This packet straddles the origin: drop its pre-origin frames, keep the rest.
                var trimFrames = Math.Min(packetFrames, -desiredStartFrame);
                byteOffset = checked((int)(trimFrames * blockAlign));
                WebcamDiagnostics.Log($"TimelineAlignedWaveProvider aligned: sourceOffsetMs={(sourceTimestamp - _origin).TotalMilliseconds:F1} latencyMs={_latency.TotalMilliseconds:F1} trimFrames={trimFrames}.");
                if (byteOffset >= count)
                {
                    return;
                }
            }
            else
            {
                // The source begins after the origin: pad the gap so it lands at the right offset.
                if (desiredStartFrame > 0)
                {
                    AddSilence(desiredStartFrame);
                }

                WebcamDiagnostics.Log($"TimelineAlignedWaveProvider aligned: sourceOffsetMs={(sourceTimestamp - _origin).TotalMilliseconds:F1} latencyMs={_latency.TotalMilliseconds:F1} padFrames={desiredStartFrame}.");
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
