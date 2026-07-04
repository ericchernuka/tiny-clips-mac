using TinyClips.Core.Models;
using TinyClips.Core.Services;

namespace TinyClips.Core.Tests;

public sealed class ClipAnalyticsServiceTests
{
    [Fact]
    public void RecordCapture_StoresCountsByTypeForTheCurrentDay()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        var settings = new TestSettingsService();
        var analytics = new ClipAnalyticsService(settings, timeProvider);

        analytics.RecordCapture(CaptureType.Screenshot);
        analytics.RecordCapture(CaptureType.Video);
        analytics.RecordCapture(CaptureType.Gif);
        analytics.RecordCapture(CaptureType.Gif);

        var day = Assert.Single(analytics.GetDailyCounts(1));
        Assert.Equal(1, day.ScreenshotCount);
        Assert.Equal(1, day.VideoCount);
        Assert.Equal(2, day.GifCount);
        Assert.Equal(4, day.TotalCount);
    }

    [Fact]
    public void GetDailyCounts_ReturnsRequestedRangeIncludingEmptyDays()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero));
        var settings = new TestSettingsService();
        var analytics = new ClipAnalyticsService(settings, timeProvider);

        analytics.RecordCapture(CaptureType.Screenshot);
        timeProvider.SetLocalNow(new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero));
        analytics.RecordCapture(CaptureType.Video);

        timeProvider.SetLocalNow(new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero));
        var days = analytics.GetDailyCounts(3);

        Assert.Collection(
            days,
            day =>
            {
                Assert.Equal(new DateTime(2026, 7, 10), day.Date);
                Assert.Equal(1, day.ScreenshotCount);
                Assert.Equal(0, day.VideoCount);
                Assert.Equal(0, day.GifCount);
            },
            day =>
            {
                Assert.Equal(new DateTime(2026, 7, 11), day.Date);
                Assert.Equal(0, day.TotalCount);
            },
            day =>
            {
                Assert.Equal(new DateTime(2026, 7, 12), day.Date);
                Assert.Equal(0, day.ScreenshotCount);
                Assert.Equal(1, day.VideoCount);
                Assert.Equal(0, day.GifCount);
            });
    }

    [Fact]
    public void PrunesDaysOlderThanThirtyDayWindow()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));
        var settings = new TestSettingsService();
        var analytics = new ClipAnalyticsService(settings, timeProvider);

        analytics.RecordCapture(CaptureType.Screenshot);
        timeProvider.SetLocalNow(new DateTimeOffset(2026, 7, 3, 8, 0, 0, TimeSpan.Zero));
        analytics.RecordCapture(CaptureType.Video);

        var days = analytics.GetDailyCounts(30);

        Assert.DoesNotContain(days, day => day.Date == new DateTime(2026, 6, 1));
        Assert.Contains(days, day => day.Date == new DateTime(2026, 7, 3) && day.VideoCount == 1);
    }

    [Fact]
    public void Clear_RemovesPersistedHistory()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 3, 8, 0, 0, TimeSpan.Zero));
        var settings = new TestSettingsService();
        var analytics = new ClipAnalyticsService(settings, timeProvider);

        analytics.RecordCapture(CaptureType.Gif);
        analytics.Clear();

        Assert.Equal(string.Empty, settings.Get("captureAnalyticsHistoryV1", string.Empty));
        Assert.Equal(0, Assert.Single(analytics.GetDailyCounts(1)).TotalCount);
    }

    [Fact]
    public void Constructor_LoadsPersistedHistoryFromSettings()
    {
        var settings = new TestSettingsService();
        settings.Set("captureAnalyticsHistoryV1", """
            {"2026-07-02":{"screenshotCount":2,"videoCount":1,"gifCount":0},"2026-07-03":{"screenshotCount":0,"videoCount":0,"gifCount":3}}
            """);
        var analytics = new ClipAnalyticsService(
            settings,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 3, 8, 0, 0, TimeSpan.Zero)));

        var days = analytics.GetDailyCounts(2);

        Assert.Equal(2, days[0].ScreenshotCount);
        Assert.Equal(1, days[0].VideoCount);
        Assert.Equal(3, days[1].GifCount);
    }

    [Fact]
    public void RecordCapture_IncrementsLifetimeTotalsAndSurvivesDayPruning()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));
        var settings = new TestSettingsService();
        var analytics = new ClipAnalyticsService(settings, timeProvider);

        analytics.RecordCapture(CaptureType.Screenshot);
        analytics.RecordCapture(CaptureType.Screenshot);
        analytics.RecordCapture(CaptureType.Video);

        // Advance well beyond the 30-day retained window so the daily bucket above is pruned.
        timeProvider.SetLocalNow(new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero));
        analytics.RecordCapture(CaptureType.Gif);

        Assert.DoesNotContain(analytics.GetDailyCounts(30), day => day.Date == new DateTime(2026, 6, 1));

        var lifetime = analytics.GetLifetimeTotals();
        Assert.Equal(2, lifetime.ScreenshotCount);
        Assert.Equal(1, lifetime.VideoCount);
        Assert.Equal(1, lifetime.GifCount);
        Assert.Equal(4, lifetime.TotalCount);
    }

    [Fact]
    public void RecordCapture_IncrementsHourlyTotalsByLocalHourAndIsNeverPruned()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero));
        var settings = new TestSettingsService();
        var analytics = new ClipAnalyticsService(settings, timeProvider);

        analytics.RecordCapture(CaptureType.Screenshot);
        analytics.RecordCapture(CaptureType.Video);

        timeProvider.SetLocalNow(new DateTimeOffset(2026, 8, 1, 9, 45, 0, TimeSpan.Zero));
        analytics.RecordCapture(CaptureType.Gif);

        timeProvider.SetLocalNow(new DateTimeOffset(2026, 8, 1, 14, 0, 0, TimeSpan.Zero));
        analytics.RecordCapture(CaptureType.Screenshot);

        var hourly = analytics.GetHourlyTotals();
        Assert.Equal(24, hourly.Count);
        Assert.Equal(3, hourly.Single(h => h.Hour == 9).Count);
        Assert.Equal(1, hourly.Single(h => h.Hour == 14).Count);
        Assert.All(hourly.Where(h => h.Hour != 9 && h.Hour != 14), h => Assert.Equal(0, h.Count));

        var busiest = analytics.GetMostActiveHour();
        Assert.NotNull(busiest);
        Assert.Equal(9, busiest.Hour);
        Assert.Equal(3, busiest.Count);
    }

    [Fact]
    public void GetWeekdayTotals_AggregatesAcrossDaysInRangeAndIdentifiesBusiestDay()
    {
        var day1 = new DateTime(2026, 7, 6); // Monday
        var day2 = new DateTime(2026, 7, 7); // Tuesday
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(day1, TimeSpan.Zero));
        var settings = new TestSettingsService();
        var analytics = new ClipAnalyticsService(settings, timeProvider);

        analytics.RecordCapture(CaptureType.Screenshot);
        analytics.RecordCapture(CaptureType.Video);

        timeProvider.SetLocalNow(new DateTimeOffset(day2, TimeSpan.Zero));
        analytics.RecordCapture(CaptureType.Gif);

        var weekdayTotals = analytics.GetWeekdayTotals(7);
        Assert.Equal(7, weekdayTotals.Count);
        Assert.Equal(2, weekdayTotals.Single(w => w.Weekday == day1.DayOfWeek).Count);
        Assert.Equal(1, weekdayTotals.Single(w => w.Weekday == day2.DayOfWeek).Count);

        var busiest = analytics.GetBusiestWeekday(7);
        Assert.NotNull(busiest);
        Assert.Equal(day1.DayOfWeek, busiest.Weekday);
        Assert.Equal(2, busiest.Count);
    }

    [Fact]
    public void GetBusiestWeekday_And_GetMostActiveHour_ReturnNullWhenNoDataRecorded()
    {
        var settings = new TestSettingsService();
        var analytics = new ClipAnalyticsService(settings, new FakeTimeProvider(new DateTimeOffset(2026, 7, 3, 8, 0, 0, TimeSpan.Zero)));

        Assert.Null(analytics.GetBusiestWeekday(7));
        Assert.Null(analytics.GetMostActiveHour());
    }

    [Fact]
    public void Clear_AlsoResetsLifetimeAndHourlyTotals()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero));
        var settings = new TestSettingsService();
        var analytics = new ClipAnalyticsService(settings, timeProvider);

        analytics.RecordCapture(CaptureType.Screenshot);
        analytics.RecordCapture(CaptureType.Video);

        analytics.Clear();

        var lifetime = analytics.GetLifetimeTotals();
        Assert.Equal(0, lifetime.TotalCount);
        Assert.All(analytics.GetHourlyTotals(), h => Assert.Equal(0, h.Count));
        Assert.Null(analytics.GetBusiestWeekday(7));
        Assert.Null(analytics.GetMostActiveHour());

        Assert.Equal(string.Empty, settings.Get("captureAnalyticsLifetimeV1", string.Empty));
        Assert.Equal(string.Empty, settings.Get("captureAnalyticsHourlyV1", string.Empty));
    }

    [Fact]
    public void GetLifetimeTotals_PersistsAcrossNewServiceInstance()
    {
        var settings = new TestSettingsService();
        var firstInstance = new ClipAnalyticsService(settings, new FakeTimeProvider(new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero)));
        firstInstance.RecordCapture(CaptureType.Screenshot);
        firstInstance.RecordCapture(CaptureType.Screenshot);
        firstInstance.RecordCapture(CaptureType.Gif);

        // Simulate the app restarting: a brand-new service instance reading from the same settings store.
        var secondInstance = new ClipAnalyticsService(settings, new FakeTimeProvider(new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero)));

        var lifetime = secondInstance.GetLifetimeTotals();
        Assert.Equal(2, lifetime.ScreenshotCount);
        Assert.Equal(0, lifetime.VideoCount);
        Assert.Equal(1, lifetime.GifCount);
    }

    private sealed class TestSettingsService : ISettingsService
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

        public AppTheme Theme { get; set; }

        public string SaveDirectory { get; set; } = string.Empty;

        public T Get<T>(string key, T defaultValue)
        {
            if (_values.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }

            return defaultValue;
        }

        public void Set<T>(string key, T value)
        {
            _values[key] = value is null ? string.Empty : value;
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _localNow;

        public FakeTimeProvider(DateTimeOffset localNow)
        {
            _localNow = localNow;
        }

        public override DateTimeOffset GetUtcNow() => _localNow.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void SetLocalNow(DateTimeOffset localNow)
        {
            _localNow = localNow;
        }
    }
}
