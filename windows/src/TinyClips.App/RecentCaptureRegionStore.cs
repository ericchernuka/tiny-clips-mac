using TinyClips.Core.Capture;

namespace TinyClips.App;

internal sealed class RecentCaptureRegionStore
{
    private readonly Dictionary<nint, PixelRect> _regionsByMonitor = new();

    public PixelRect? Get(nint hMonitor)
    {
        return _regionsByMonitor.TryGetValue(hMonitor, out var region)
            ? region
            : null;
    }

    public IReadOnlyDictionary<nint, PixelRect> Snapshot()
    {
        return new Dictionary<nint, PixelRect>(_regionsByMonitor);
    }

    public void Save(nint hMonitor, PixelRect region)
    {
        _regionsByMonitor[hMonitor] = region;
    }

    public void Clear()
    {
        _regionsByMonitor.Clear();
    }
}
