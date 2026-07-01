import CoreGraphics

@MainActor
final class SessionRecentCaptureRegionStore {
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
