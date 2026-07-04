using TinyClips.Core.Models;

namespace TinyClips.Core.Services;

public interface IClipAnalyticsService
{
    void RecordCapture(CaptureType type);
    IReadOnlyList<DailyCaptureAnalytics> GetDailyCounts(int days);

    /// <summary>All-time totals by capture type, never pruned by the rolling day window.</summary>
    LifetimeCaptureAnalytics GetLifetimeTotals();

    /// <summary>Aggregate totals by day of week (all capture types combined), over the given day range.</summary>
    IReadOnlyList<WeekdayCaptureTotal> GetWeekdayTotals(int days);

    /// <summary>The single busiest day of week over the given range, or null if there is no data yet.</summary>
    WeekdayCaptureTotal? GetBusiestWeekday(int days);

    /// <summary>All-time (never pruned) totals for each hour of the day, 0-23.</summary>
    IReadOnlyList<HourCaptureTotal> GetHourlyTotals();

    /// <summary>The single most active hour of the day, all-time, or null if there is no data yet.</summary>
    HourCaptureTotal? GetMostActiveHour();

    void Clear();
}
