using System.Diagnostics;

namespace TinyClips.Core.Capture;

/// <summary>
/// A shared recording clock expressed in the system-relative QPC time domain used by
/// MediaFrameReference and WASAPI capture timestamps.
/// </summary>
internal sealed class RecordingTimeline
{
    private readonly object _gate = new();
    private TimeSpan _pausedDuration;
    private TimeSpan? _pauseStartedAt;

    private RecordingTimeline(TimeSpan origin)
    {
        Origin = origin;
    }

    public TimeSpan Origin { get; }

    public TimeSpan Elapsed => Normalize(GetSystemRelativeTime());

    public static RecordingTimeline StartNow() => new(GetSystemRelativeTime());

    internal static RecordingTimeline FromOrigin(TimeSpan origin) => new(origin);

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _pauseStartedAt is not null;
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            _pauseStartedAt ??= GetSystemRelativeTime();
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_pauseStartedAt is { } pausedAt)
            {
                _pausedDuration += GetSystemRelativeTime() - pausedAt;
                _pauseStartedAt = null;
            }
        }
    }

    public TimeSpan Normalize(TimeSpan sourceTimestamp)
    {
        lock (_gate)
        {
            var paused = _pausedDuration;
            if (_pauseStartedAt is { } pausedAt)
            {
                paused += sourceTimestamp > pausedAt ? sourceTimestamp - pausedAt : TimeSpan.Zero;
            }

            return sourceTimestamp - Origin - paused;
        }
    }

    private static TimeSpan GetSystemRelativeTime() =>
        Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());
}
