# Plan 001: Preselect remembered regions on every display

> **Executor instructions**: Follow this plan step by step. Run every verification command and confirm the expected result before moving to the next step. If anything in the "STOP conditions" section occurs, stop and report. When done, update the status row for this plan in `advisor-plans/README.md` unless a reviewer told you they maintain the index.
>
> **Drift check (run first)**: `git diff --stat eeb1f79..HEAD -- mac/TinyClips/CaptureManager.swift mac/TinyClips/Capture/RegionSelector.swift mac/TinyClips/Models/RecentCaptureRegionStore.swift`
>
> If any in-scope file changed since this plan was written, compare the "Current state" excerpts against the live code before proceeding. On a mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: S
- **Risk**: LOW
- **Depends on**: none
- **Category**: bug
- **Planned at**: commit `eeb1f79`, 2026-07-01

## Why This Matters

The branch introduces a session-only region store keyed by display ID, which implies TinyClips can remember a different manual region for each display. The current call path only passes the region for the display under the mouse cursor, so the all-display selector cannot preselect saved regions on other displays. This is especially noticeable when the user has a remembered region on monitor B but opens the selector while the cursor is on monitor A.

## Current State

Relevant files:

- `mac/TinyClips/Models/RecentCaptureRegionStore.swift` - in-memory session store keyed by `CGDirectDisplayID`.
- `mac/TinyClips/CaptureManager.swift` - capture coordinator; chooses remembered region before launching `RegionSelector`.
- `mac/TinyClips/Capture/RegionSelector.swift` - displays one overlay window per screen for all-display region selection.

Current excerpts:

```swift
// mac/TinyClips/Models/RecentCaptureRegionStore.swift:4
final class SessionRecentCaptureRegionStore {
    private var regionsByDisplayID: [CGDirectDisplayID: CaptureRegion] = [:]

    func region(for displayID: CGDirectDisplayID) -> CaptureRegion? {
        regionsByDisplayID[displayID]
    }
```

```swift
// mac/TinyClips/CaptureManager.swift:196
guard let region = await RegionSelector.selectRegion(recentRegion: recentRegionForCurrentDisplay()) else {
    recentRegionStore.clear()
    if shouldReturnToPicker {
        showScreenshotPicker()
    }
    return
}
```

```swift
// mac/TinyClips/CaptureManager.swift:1478
private func recentRegionForCurrentDisplay() -> CaptureRegion? {
    guard let screen = screenUnderMouseCursor() ?? NSScreen.main,
          let displayID = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? CGDirectDisplayID
    else {
        return nil
    }

    return recentRegionStore.region(for: displayID)
}
```

```swift
// mac/TinyClips/Capture/RegionSelector.swift:38
func show() {
    let screens = targetScreen.map { [$0] } ?? NSScreen.screens
    for screen in screens {
        ...
        let view = RegionSelectionView(frame: NSRect(origin: .zero, size: screen.frame.size))
        view.preselect(recentRegion, on: screen)
```

Repo conventions to follow:

- UI-facing mac classes are `@MainActor` where appropriate.
- Keep region-selection state in `CaptureRegion`, which already includes `displayID`, display-local `sourceRect`, and `scaleFactor`.
- Use callback/async bridging patterns already in `RegionSelector.selectRegion(...)`; do not introduce `NotificationCenter`.

## Commands You Will Need

| Purpose | Command | Expected on success |
|---------|---------|---------------------|
| Drift check | `git diff --stat eeb1f79..HEAD -- mac/TinyClips/CaptureManager.swift mac/TinyClips/Capture/RegionSelector.swift mac/TinyClips/Models/RecentCaptureRegionStore.swift` | No output if no drift |
| Whitespace | `git diff --check` | exit 0, no output |
| Direct mac build | `xcodebuild build -project mac/TinyClips.xcodeproj -scheme TinyClips -configuration Debug -derivedDataPath /tmp/tiny-clips-plan-001-direct CODE_SIGN_IDENTITY= CODE_SIGNING_REQUIRED=NO CODE_SIGNING_ALLOWED=NO` | exit 0, ends with `** BUILD SUCCEEDED **` |
| MAS mac build | `xcodebuild build -project mac/TinyClips.xcodeproj -scheme TinyClipsMAS -configuration Debug -derivedDataPath /tmp/tiny-clips-plan-001-mas CODE_SIGN_IDENTITY= CODE_SIGNING_REQUIRED=NO CODE_SIGNING_ALLOWED=NO` | exit 0, ends with `** BUILD SUCCEEDED **` |

## Scope

**In scope**:

- `mac/TinyClips/Models/RecentCaptureRegionStore.swift`
- `mac/TinyClips/CaptureManager.swift`
- `mac/TinyClips/Capture/RegionSelector.swift`
- `advisor-plans/README.md`

**Out of scope**:

- `windows/**` - this branch is intentionally mac-only.
- Persisting remembered regions across launches.
- Adding a settings toggle.
- Changing region clear semantics on cancel.
- Editing unrelated capture modes such as window or screen capture.

## Git Workflow

- Branch: use the current branch unless instructed otherwise. If creating a new branch, use the repo/user prefix `ec/`, for example `ec/remembered-regions-display-scope`.
- Commit message style: short imperative sentence. Recent examples: `Add region setup cancel and reselect flow`, `Remember manual capture regions for this session`.
- Do not push or open a PR unless the operator instructs it.

## Steps

### Step 1: Expose a snapshot of all remembered regions

In `mac/TinyClips/Models/RecentCaptureRegionStore.swift`, add a method that returns the stored dictionary for selection:

```swift
func regionsByDisplayIDSnapshot() -> [CGDirectDisplayID: CaptureRegion] {
    regionsByDisplayID
}
```

Keep `region(for:)`, `save(_:)`, and `clear()` unless they become unused after the rest of the change.

**Verify**: `git diff --check` -> exit 0.

### Step 2: Change RegionSelector from singular region to display-keyed regions

In `mac/TinyClips/Capture/RegionSelector.swift`:

1. Change the public API from `recentRegion: CaptureRegion?` to `recentRegionsByDisplayID: [CGDirectDisplayID: CaptureRegion] = [:]`.
2. Store the dictionary on `RegionSelectorController`.
3. In `RegionSelectorController.show()`, for each `screen`, resolve that screen's `CGDirectDisplayID` and pass only the matching `CaptureRegion` into `view.preselect(...)`.

Target shape:

```swift
static func selectRegion(recentRegionsByDisplayID: [CGDirectDisplayID: CaptureRegion] = [:]) async -> CaptureRegion? {
    return await selectRegion(on: nil, recentRegionsByDisplayID: recentRegionsByDisplayID)
}
```

Inside the `for screen in screens` loop:

```swift
let displayID = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? CGDirectDisplayID
let recentRegion = displayID.flatMap { recentRegionsByDisplayID[$0] }
view.preselect(recentRegion, on: screen)
```

Leave `RegionSelectionView.preselect(_:, on:)` display-ID guard in place as a defensive check.

**Verify**: `rg "recentRegion:" mac/TinyClips/Capture/RegionSelector.swift` -> no matches.

### Step 3: Pass all remembered regions from CaptureManager

In `mac/TinyClips/CaptureManager.swift`, replace calls like:

```swift
RegionSelector.selectRegion(recentRegion: recentRegionForCurrentDisplay())
```

with:

```swift
RegionSelector.selectRegion(recentRegionsByDisplayID: recentRegionStore.regionsByDisplayIDSnapshot())
```

There are two call sites: screenshot region capture and recording target selection.

Then remove `recentRegionForCurrentDisplay()` if it has no remaining callers.

**Verify**:

- `rg "recentRegionForCurrentDisplay|recentRegion:" mac/TinyClips` -> no matches.
- `rg "regionsByDisplayIDSnapshot" mac/TinyClips` -> shows one definition and two call sites.

### Step 4: Build both mac schemes

Run the commands in "Commands You Will Need":

1. `git diff --check`
2. Direct mac build
3. MAS mac build

**Verify**: both builds end with `** BUILD SUCCEEDED **`.

## Test Plan

There is no mac test target in this repo. Build verification is the available automated gate.

Manual regression checks for the human reviewer:

- Save a manual region on display A, then save a different manual region on display B.
- Invoke region selection with the cursor on display A; display A should show its remembered rectangle and display B should also show its remembered rectangle.
- Invoke region selection with the cursor on display B; both displays should still preselect their own remembered rectangles.
- Press Esc from the selector; remembered regions should clear according to current branch semantics.

## Done Criteria

- [ ] `rg "recentRegionForCurrentDisplay|recentRegion:" mac/TinyClips` returns no matches.
- [ ] `rg "regionsByDisplayIDSnapshot" mac/TinyClips` shows one store method and two `CaptureManager` call sites.
- [ ] `git diff --check` exits 0.
- [ ] `xcodebuild` for `TinyClips` exits 0.
- [ ] `xcodebuild` for `TinyClipsMAS` exits 0.
- [ ] No files outside the in-scope list are modified.
- [ ] `advisor-plans/README.md` status row for Plan 001 is updated.

## STOP Conditions

Stop and report back if:

- The in-scope files no longer match the current-state excerpts.
- `RegionSelector` has gained additional public call sites that require a compatibility API decision.
- The fix appears to require changing Windows code.
- Either Xcode build fails twice after one reasonable fix attempt.

## Maintenance Notes

This plan aligns the API shape with the session store's display-keyed design. Reviewers should check that cancel still clears the store and that saving still happens only on screenshot capture or recording start, not on provisional recording-region selection.

