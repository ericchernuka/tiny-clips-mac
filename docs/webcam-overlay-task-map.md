# Webcam Overlay Implementation Task Map

This map aligns implementation work with the current todo set.

## macOS track

| Todo ID | Scope | Expected output |
|---|---|---|
| `mac-settings` | Webcam capture/overlay settings model | Persisted options and sane defaults for overlay behavior |
| `mac-devices` | Camera device discovery | Enumerated webcam list with stable selection identity |
| `mac-perms` | Camera permission flow | Clear request/denied handling and screen-only fallback |
| `mac-capture` | Webcam capture controller | Timestamped webcam frame stream for recording session |
| `mac-export` | Post-process compositor integration | Final MP4 with webcam overlay applied from dual-track inputs |
| `mac-ui` | Settings + recording UI affordances | User controls for enable, camera, size, position, shape |
| `mac-build` | Validation | Build verification for `TinyClips` and `TinyClipsMAS` schemes |

## Windows track

| Todo ID | Scope | Expected output |
|---|---|---|
| `win-settings` | Webcam capture/overlay settings model | Persisted overlay config aligned with Windows defaults |
| `win-devices` | Webcam enumeration | Device catalog and selection persistence |
| `win-perms` | Webcam capability and permission handling | Capability declared + runtime behavior verified |
| `win-capture` | MediaCapture/MediaFrameReader pipeline | Timestamped webcam frame ingestion for session |
| `win-compositor` | CPU in-pipeline compositor | MP4 output with webcam overlay blended directly into BGRA capture frames before encoding |
| `win-ui` | WinUI controls and state surfacing | User-facing webcam overlay controls and status |
| `win-build` | Validation | `dotnet restore/build/test` pass for Windows solution/projects |

## Documentation and release track

| Todo ID | Scope | Expected output |
|---|---|---|
| `docs-plan-research` | Approved plan + deep research docs | Cross-platform architecture/roadmap docs under `docs/` |
| `docs-changelog` | Release notes alignment | Updated changelog entries for shipped webcam-overlay work |

## Dependency order (recommended)

1. Settings + devices + permissions (platform foundations).
2. Capture pipeline.
3. Compositor/export integration.
4. UI wiring.
5. Build/test validation.
6. Changelog/doc finalization.

## Definition of done for webcam overlay delivery

- MVP MP4 overlay works on macOS and Windows with platform-appropriate defaults.
- Failure paths degrade cleanly to screen-only recording.
- Platform build/test validation passes for touched areas.
- Docs and changelog reflect implemented scope and known limitations.
