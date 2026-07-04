namespace TinyClips.Core.Models;

public sealed record DailyCaptureAnalytics(
    DateTime Date,
    int ScreenshotCount,
    int VideoCount,
    int GifCount)
{
    public int TotalCount => ScreenshotCount + VideoCount + GifCount;
}

/// <summary>All-time (never pruned) capture counts by type.</summary>
public sealed record LifetimeCaptureAnalytics(
    int ScreenshotCount,
    int VideoCount,
    int GifCount)
{
    public int TotalCount => ScreenshotCount + VideoCount + GifCount;
}

/// <summary>Aggregate capture total for a single day of the week, across all capture types.</summary>
public sealed record WeekdayCaptureTotal(DayOfWeek Weekday, int Count);

/// <summary>All-time (never pruned) aggregate capture total for a single hour of the day (0-23).</summary>
public sealed record HourCaptureTotal(int Hour, int Count);
