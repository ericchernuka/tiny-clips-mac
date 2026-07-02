using System.Diagnostics;

namespace TinyClips.Core.Capture;

/// <summary>
/// A shared recording clock expressed in the system-relative QPC time domain used by
/// MediaFrameReference and WASAPI capture timestamps.
/// </summary>
internal sealed class RecordingTimeline
{
    private RecordingTimeline(TimeSpan origin)
    {
        Origin = origin;
    }

    public TimeSpan Origin { get; }

    public TimeSpan Elapsed => Normalize(GetSystemRelativeTime());

    public static RecordingTimeline StartNow() => new(GetSystemRelativeTime());

    internal static RecordingTimeline FromOrigin(TimeSpan origin) => new(origin);

    public TimeSpan Normalize(TimeSpan sourceTimestamp) => sourceTimestamp - Origin;

    private static TimeSpan GetSystemRelativeTime() =>
        Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());
}
