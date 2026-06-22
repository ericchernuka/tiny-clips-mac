# Webcam Overlay Plan (Cross-Platform)

This document records the approved webcam-overlay direction for TinyClips on macOS and Windows.

## Cross-platform feature overview and MVP scope

### Goal

Add an optional webcam picture-in-picture overlay for screen recordings while keeping TinyClips tray/menu-bar-first, lightweight, and stable.

### Phase 1 MVP decisions

- Ship webcam overlay for **video (MP4)** recordings first.
- Use platform-appropriate compositing:
  - macOS records screen and webcam streams separately, then composites them during MP4 export.
  - Windows blends webcam frames into the BGRA screen frames during the capture/encode pipeline before MP4 encoding.
- Keep controls intentionally small for MVP:
  - enable/disable webcam overlay
  - camera device selection
  - position preset (for example: top-left/top-right/bottom-left/bottom-right)
  - size preset (small/medium/large)
  - shape preset (rectangle/rounded/circle when supported)
- Preserve current capture and save flows; webcam is additive and optional.
- Defer animated/live interaction controls to a later phase.
- Defer GIF webcam overlay to Phase 3.

## macOS architecture decision

### Decision

Use a **dual-track, post-process composite pipeline**:

1. Record display content with the existing ScreenCaptureKit-based pipeline.
2. Capture webcam frames in parallel to a separate webcam track.
3. Composite screen + webcam in post-processing to produce final MP4.

### Why this direction

- Minimizes risk to the existing screen recorder path.
- Keeps capture-time UI and encoding complexity lower for MVP.
- Gives deterministic overlay placement and sizing in final export.

### Key risks and gotchas

- **Clock sync/drift:** screen and webcam timelines must use consistent timestamps and drift handling.
- **Frame drops/backpressure:** webcam frame flow must not block screen recording throughput.
- **Retina/scale handling:** overlay math must remain pixel-accurate across mixed-scale displays.
- **Orientation/mirroring:** front camera orientation and optional mirroring must stay predictable.
- **Permissions UX:** camera permission denial/revocation must fail gracefully without breaking screen-only recording.
- **Export cost:** post-process compositing adds CPU/GPU time and temporary file pressure.

See also: [docs/retina-display-capture.md](retina-display-capture.md).

## Windows architecture decision

### Decision

Use **MediaCapture + MediaFrameReader** for webcam ingestion and a **CPU compositor** in the capture/encode pipeline:

1. Record screen content using the current Windows capture/recording path.
2. Read webcam frames through MediaCapture/MediaFrameReader alongside the recording session.
3. Blend the latest webcam frame into each BGRA capture frame before the frame is handed to the encoder for Phase 1 MP4 output.

### Why this direction

- Works with current app architecture and keeps MVP implementation explicit and debuggable.
- Avoids high-risk real-time GPU composition changes in Phase 1 while keeping the final encoded output single-track.
- Enables straightforward phased expansion into live controls and GIF support.

### Key risks and gotchas

- **Frame freshness:** webcam frame selection must avoid stale or jumpy overlay playback.
- **Pixel format conversion:** conversion/copy paths can become hot spots if not bounded.
- **CPU budget:** compositor cost can impact capture/encode throughput on lower-end hardware.
- **DPI/coordinate correctness:** overlay placement must remain stable under mixed DPI displays.
- **Device lifecycle:** camera unplug/sleep/resume and busy-device cases need resilient recovery.
- **Capabilities/privacy:** app capability and runtime permission states must be handled clearly.

See also: [windows/docs/dpi-and-coordinates.md](../windows/docs/dpi-and-coordinates.md).

## Phased roadmap

### Phase 1 — MVP (webcam overlay for MP4)

- Cross-platform webcam settings model + defaults.
- Device enumeration and selection.
- Webcam capture pipeline per platform.
- Platform compositor producing final MP4 with overlay: post-process export on macOS, in-pipeline BGRA composition on Windows.
- Basic overlay presets (position/size/shape).
- Error handling and fallback to screen-only recording.

### Phase 2 — Live controls and UX depth

- In-session/live controls (toggle, position, size, mirror) during recording.
- Better preview affordances and state visibility.
- More resilient runtime handling for camera changes/interruption.
- Performance tuning and quality improvements.

### Phase 3 — GIF support

- Apply webcam overlay pipeline to GIF export path.
- Add GIF-specific sizing/perf limits to keep file size bounded.
- Validate quality/perf tradeoffs for short-loop captures.
