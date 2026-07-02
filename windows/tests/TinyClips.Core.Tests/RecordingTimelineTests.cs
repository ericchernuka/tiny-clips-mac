using NAudio.Wave;
using TinyClips.Core.Capture;

namespace TinyClips.Core.Tests;

public sealed class RecordingTimelineTests
{
    [Fact]
    public void Normalize_UsesSharedSystemRelativeOrigin()
    {
        var origin = TimeSpan.FromSeconds(42);
        var timeline = RecordingTimeline.FromOrigin(origin);

        Assert.Equal(TimeSpan.FromMilliseconds(125), timeline.Normalize(origin + TimeSpan.FromMilliseconds(125)));
        Assert.Equal(TimeSpan.FromMilliseconds(-25), timeline.Normalize(origin - TimeSpan.FromMilliseconds(25)));
    }

    [Fact]
    public void AlignedProviders_PreserveDifferentSourceStartOffsets()
    {
        var format = new WaveFormat(1000, 16, 1);
        var origin = TimeSpan.FromSeconds(10);
        var first = new TimelineAlignedWaveProvider(format);
        var second = new TimelineAlignedWaveProvider(format);
        first.BeginTimeline(origin);
        second.BeginTimeline(origin);

        first.AddSamples(ToBytes(100, 101), 4, origin);
        second.AddSamples(ToBytes(200, 201), 4, origin + TimeSpan.FromMilliseconds(2));

        Assert.Equal(new short[] { 100, 101, 0, 0 }, ReadSamples(first, 4));
        Assert.Equal(new short[] { 0, 0, 200, 201 }, ReadSamples(second, 4));
    }

    [Fact]
    public void AddSamples_TrimsTimestampOverlapWithoutShiftingFollowingAudio()
    {
        var format = new WaveFormat(1000, 16, 1);
        var origin = TimeSpan.FromSeconds(10);
        var provider = new TimelineAlignedWaveProvider(format);
        provider.BeginTimeline(origin);
        provider.AddSamples(ToBytes(1, 2, 3, 4), 8, origin);

        Assert.Equal(new short[] { 1, 2 }, ReadSamples(provider, 2));

        provider.AddSamples(ToBytes(9, 10, 11), 6, origin + TimeSpan.FromMilliseconds(3));

        Assert.Equal(new short[] { 3, 4, 10, 11 }, ReadSamples(provider, 4));
    }

    private static byte[] ToBytes(params short[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(short)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static short[] ReadSamples(IWaveProvider provider, int count)
    {
        var bytes = new byte[count * sizeof(short)];
        Assert.Equal(bytes.Length, provider.Read(bytes, 0, bytes.Length));
        var samples = new short[count];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        return samples;
    }
}
