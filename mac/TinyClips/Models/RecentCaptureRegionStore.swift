import CoreGraphics

@MainActor
protocol RecentCaptureRegionStore: AnyObject {
    func region(for displayID: CGDirectDisplayID) -> CaptureRegion?
    func save(_ region: CaptureRegion)
    func clear()
}

@MainActor
final class SessionRecentCaptureRegionStore: RecentCaptureRegionStore {
    private var regionsByDisplayID: [CGDirectDisplayID: CaptureRegion] = [:]

    func region(for displayID: CGDirectDisplayID) -> CaptureRegion? {
        regionsByDisplayID[displayID]
    }

    func save(_ region: CaptureRegion) {
        regionsByDisplayID[region.displayID] = region
    }

    func clear() {
        regionsByDisplayID.removeAll()
    }
}
