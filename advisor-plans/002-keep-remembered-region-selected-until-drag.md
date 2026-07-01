# Plan 002: Keep remembered region selected until the user drags

> **Executor instructions**: Follow this plan step by step. Run every verification command and confirm the expected result before moving to the next step. If anything in the "STOP conditions" section occurs, stop and report. When done, update the status row for this plan in `advisor-plans/README.md` unless a reviewer told you they maintain the index.
>
> **Drift check (run first)**: `git diff --stat eeb1f79..HEAD -- mac/TinyClips/Capture/RegionSelector.swift`
>
> If this file changed since this plan was written, compare the "Current state" excerpts against the live code before proceeding. On a mismatch, treat it as a STOP condition. If Plan 001 was executed first, expect drift in this file; reconcile only the API/display-scope differences from Plan 001, then continue.

## Status

- **Priority**: P1
- **Effort**: S
- **Risk**: LOW
- **Depends on**: `advisor-plans/001-preselect-remembered-regions-on-every-display.md`
- **Category**: bug
- **Planned at**: commit `eeb1f79`, 2026-07-01

## Why This Matters

The branch intends remembered regions to behave like real selections: users should be able to confirm one quickly or redraw it. Enter currently confirms the remembered rectangle, but double-click does not work reliably because the first click clears `selectedRect`. Fixing this makes the interaction match the visible UI state: the rectangle remains selected until the user actually drags a new rectangle.

## Current State

Relevant file:

- `mac/TinyClips/Capture/RegionSelector.swift` - overlay view that draws and completes manual region selections.

Current excerpts:

```swift
// mac/TinyClips/Capture/RegionSelector.swift:215
override func mouseDown(with event: NSEvent) {
    let point = clampedPoint(convert(event.locationInWindow, from: nil))
    if event.clickCount >= 2, let selectedRect, selectedRect.contains(point) {
        completeSelection(selectedRect)
        return
    }

    selectedRect = nil
    startPoint = point
    currentPoint = point
    needsDisplay = true
}
```

```swift
// mac/TinyClips/Capture/RegionSelector.swift:228
override func mouseDragged(with event: NSEvent) {
    currentPoint = clampedPoint(convert(event.locationInWindow, from: nil))
    needsDisplay = true
}
```

```swift
// mac/TinyClips/Capture/RegionSelector.swift:233
override func mouseUp(with event: NSEvent) {
    guard let start = startPoint else { return }
    let end = clampedPoint(convert(event.locationInWindow, from: nil))
    let selectionRect = makeRect(from: start, to: end)
```

Problem: on the first click inside `selectedRect`, `event.clickCount` is usually `1`, so the method clears `selectedRect`. The second click can no longer satisfy `let selectedRect, selectedRect.contains(point)`.

Repo conventions to follow:

- Keep the overlay implementation local to `RegionSelector.swift`.
- Use simple view-local state instead of notifications or global state.
- Preserve Escape cancel and Enter confirm behavior.

## Commands You Will Need

| Purpose | Command | Expected on success |
|---------|---------|---------------------|
| Drift check | `git diff --stat eeb1f79..HEAD -- mac/TinyClips/Capture/RegionSelector.swift` | No unexpected drift beyond Plan 001 if it already landed |
| Whitespace | `git diff --check` | exit 0, no output |
| Direct mac build | `xcodebuild build -project mac/TinyClips.xcodeproj -scheme TinyClips -configuration Debug -derivedDataPath /tmp/tiny-clips-plan-002-direct CODE_SIGN_IDENTITY= CODE_SIGNING_REQUIRED=NO CODE_SIGNING_ALLOWED=NO` | exit 0, ends with `** BUILD SUCCEEDED **` |
| MAS mac build | `xcodebuild build -project mac/TinyClips.xcodeproj -scheme TinyClipsMAS -configuration Debug -derivedDataPath /tmp/tiny-clips-plan-002-mas CODE_SIGN_IDENTITY= CODE_SIGNING_REQUIRED=NO CODE_SIGNING_ALLOWED=NO` | exit 0, ends with `** BUILD SUCCEEDED **` |

## Scope

**In scope**:

- `mac/TinyClips/Capture/RegionSelector.swift`
- `advisor-plans/README.md`

**Out of scope**:

- `mac/TinyClips/CaptureManager.swift` unless resolving drift from Plan 001 requires reading it.
- `mac/TinyClips/Models/RecentCaptureRegionStore.swift`.
- Windows code.
- Changing remembered-region save or clear semantics.
- Adding new keyboard shortcuts.

## Git Workflow

- Branch: use the current branch unless instructed otherwise. If creating a new branch, use the repo/user prefix `ec/`, for example `ec/fix-remembered-region-confirm`.
- Commit message style: short imperative sentence. Recent examples: `Add region setup cancel and reselect flow`, `Remember manual capture regions for this session`.
- Do not push or open a PR unless the operator instructs it.

## Steps

### Step 1: Add state that distinguishes click-preserve from drag-redraw

In `RegionSelectionView`, add a private Boolean near `selectedRect`:

```swift
private var isDrawingSelection = false
```

This flag should mean: the user is actively drawing a replacement rectangle. A plain click inside the remembered rectangle should not set it.

**Verify**: `rg "isDrawingSelection" mac/TinyClips/Capture/RegionSelector.swift` -> shows the new property.

### Step 2: Preserve selectedRect on simple clicks inside it

Update `mouseDown(with:)` so it follows this behavior:

- If `event.clickCount >= 2` and the click is inside `selectedRect`, call `completeSelection(selectedRect)` and return.
- If the click is inside `selectedRect` but is not a double-click, set `startPoint = point`, set `currentPoint = nil`, set `isDrawingSelection = false`, and do not clear `selectedRect`.
- Otherwise, clear `selectedRect`, set `startPoint = point`, set `currentPoint = point`, set `isDrawingSelection = true`, and redraw.

Target shape:

```swift
let point = clampedPoint(convert(event.locationInWindow, from: nil))
if let selectedRect, selectedRect.contains(point) {
    if event.clickCount >= 2 {
        completeSelection(selectedRect)
        return
    }
    startPoint = point
    currentPoint = nil
    isDrawingSelection = false
    return
}

selectedRect = nil
startPoint = point
currentPoint = point
isDrawingSelection = true
needsDisplay = true
```

**Verify**: `sed -n '210,235p' mac/TinyClips/Capture/RegionSelector.swift` -> simple click inside `selectedRect` does not clear `selectedRect`.

### Step 3: Start redraw only once the user drags

Update `mouseDragged(with:)` so dragging from inside a remembered rectangle switches into redraw mode:

```swift
guard startPoint != nil else { return }
if !isDrawingSelection {
    selectedRect = nil
    isDrawingSelection = true
}
currentPoint = clampedPoint(convert(event.locationInWindow, from: nil))
needsDisplay = true
```

This preserves the remembered rectangle for a click, but clears it as soon as the user actually drags.

**Verify**: `sed -n '228,245p' mac/TinyClips/Capture/RegionSelector.swift` -> `selectedRect = nil` happens in drag transition, not unconditionally in `mouseDown`.

### Step 4: Ignore mouseUp for click-preserve events

Update `mouseUp(with:)` so a click inside the remembered rectangle does not try to create a tiny new selection:

```swift
guard isDrawingSelection, let start = startPoint else {
    startPoint = nil
    currentPoint = nil
    return
}
```

When a new selection completes, `completeSelection(_:)` should reset `isDrawingSelection = false` along with `startPoint` and `currentPoint`.

**Verify**: `sed -n '233,270p' mac/TinyClips/Capture/RegionSelector.swift` -> `mouseUp` guards on `isDrawingSelection`, and `completeSelection` resets the flag.

### Step 5: Build both mac schemes

Run:

1. `git diff --check`
2. Direct mac build
3. MAS mac build

**Verify**: both builds end with `** BUILD SUCCEEDED **`.

## Test Plan

There is no mac test target in this repo. Build verification is the available automated gate.

Manual regression checks for the human reviewer:

- Open region selection with a remembered rectangle visible.
- Single-click inside the remembered rectangle and release: the rectangle should remain visible and selected.
- Double-click inside the remembered rectangle: selection should complete using that rectangle.
- Click and drag from inside the remembered rectangle: the old rectangle should clear and the new drag rectangle should appear.
- Press Enter with the remembered rectangle visible: selection should complete.
- Press Esc: selection should cancel and current branch clear semantics should still run.

## Done Criteria

- [ ] `selectedRect = nil` is no longer unconditional in `mouseDown(with:)`.
- [ ] `mouseDragged(with:)` transitions from preserved remembered selection to redraw mode.
- [ ] `mouseUp(with:)` ignores non-drawing click-preserve interactions.
- [ ] `completeSelection(_:)` resets drawing state.
- [ ] `git diff --check` exits 0.
- [ ] `xcodebuild` for `TinyClips` exits 0.
- [ ] `xcodebuild` for `TinyClipsMAS` exits 0.
- [ ] No files outside the in-scope list are modified.
- [ ] `advisor-plans/README.md` status row for Plan 002 is updated.

## STOP Conditions

Stop and report back if:

- `RegionSelectionView` has been substantially refactored and the current-state excerpts no longer map to the live code.
- Fixing double-click appears to require changing `CaptureManager` or the region store.
- You discover AppKit double-click events are not delivered to this overlay window after the state fix; report that behavior instead of adding a new shortcut.
- Either Xcode build fails twice after one reasonable fix attempt.

## Maintenance Notes

Reviewers should focus on the interaction between `mouseDown`, `mouseDragged`, and `mouseUp`: a click should preserve and allow double-click confirmation, while a drag should immediately enter redraw mode. This plan intentionally does not add keyboard shortcuts or change Enter/Escape behavior.

