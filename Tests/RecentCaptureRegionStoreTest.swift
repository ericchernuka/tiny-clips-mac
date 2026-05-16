import CoreGraphics
import Foundation

@inline(__always)
private func assertEqual<T: Equatable>(_ actual: T, _ expected: T, _ message: String) {
    guard actual == expected else {
        fatalError("\(message): expected \(expected), got \(actual)")
    }
}

let store = SessionRecentCaptureRegionStore()
let firstDisplay: CGDirectDisplayID = 101
let secondDisplay: CGDirectDisplayID = 202

let firstRegion = CaptureRegion(
    sourceRect: CGRect(x: 10, y: 20, width: 300, height: 180),
    displayID: firstDisplay,
    scaleFactor: 2
)
let secondRegion = CaptureRegion(
    sourceRect: CGRect(x: 5, y: 7, width: 90, height: 44),
    displayID: secondDisplay,
    scaleFactor: 1
)

store.save(firstRegion)
store.save(secondRegion)

assertEqual(store.region(for: firstDisplay)?.sourceRect, firstRegion.sourceRect, "stores regions per display")
assertEqual(store.region(for: secondDisplay)?.sourceRect, secondRegion.sourceRect, "does not overwrite other displays")

store.clear()

assertEqual(store.region(for: firstDisplay), nil, "clear removes first display")
assertEqual(store.region(for: secondDisplay), nil, "clear removes second display")
