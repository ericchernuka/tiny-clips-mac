# Audio/Video Sync on Windows

Tiny Clips for Windows records the **screen**, an optional **webcam overlay**, **microphone**,
and **system (loopback) audio** as one MP4. These come from four independent capture sources
that each start at slightly different times and run on different clocks, so keeping them in sync —
and keeping the audio clean — takes deliberate timeline handling. This is the Windows analogue of
the macOS shared-`CMTime`/`AVAssetWriter` approach.

## The pieces

| Source | API | Clock |
| --- | --- | --- |
| Screen | `Windows.Graphics.Capture` (WGC) | system-relative QPC |
| Webcam overlay | `MediaCapture` frame reader | `MediaFrameReference` QPC |
| Microphone | WASAPI capture (`TimestampedWasapiCapture`) | WASAPI packet QPC |
| System audio | WASAPI loopback (`TimestampedWasapiCapture`) | WASAPI packet QPC |

All four are muxed into one MP4 by a `MediaStreamSource` + `MediaTranscoder`: the composited
BGRA video frames feed the video stream descriptor, and the mixed 48 kHz / 16‑bit / stereo PCM
feeds the audio stream descriptor.

## One shared origin

Every source is anchored to a single **`RecordingTimeline`** — a QPC-relative origin captured with
`RecordingTimeline.StartNow()` at the moment recording truly begins (after the encoder is prepared
and the first webcam frame is warm). See `VideoRecordingService.StartAsync`.

- **Screen** frames get `pts = timeline.Elapsed` (real wall-clock elapsed since origin) at emit
  time, produced at a steady cadence by the pump in `ContinuousCaptureSession`.
- **Webcam** frames are normalized against the same origin before compositing, and a frame is never
  composited from ahead of the screen clock (`IsWebcamFrameReady`).
- **Audio** packets keep their real WASAPI capture timestamps and are aligned to the same origin by
  `TimelineAlignedWaveProvider` (below).

Anchoring to the real start moment (rather than PTS 0 = whatever arrives first) keeps encoder
warm-up and camera spin-up out of the recorded timeline — otherwise several seconds of frozen
pre-roll get baked into the front of every clip.

## Aligning audio to the timeline — `TimelineAlignedWaveProvider`

Each audio source has its own `TimelineAlignedWaveProvider` that places captured packets on the
shared timeline, then the two are combined by an NAudio `MixingSampleProvider`. The alignment rules
were hard-won; each exists to fix a specific defect:

1. **Align only the first packet, then append contiguously.** Re-deriving every ~10 ms packet's
   position from its (jittery, frame-rounded) QPC timestamp inserted or dropped a sample or two on
   *every* packet — ~100 micro-edits per second of constant audible crackle. Only the first packet
   after the origin is positioned; the rest are appended back-to-back.

2. **Drop *all* pre-origin pre-roll, not just the first straddling packet.** The capture thread
   starts before the origin, so several whole packets can predate it. Each fully pre-origin packet is
   dropped; alignment locks onto the first packet that reaches the origin (trimming its pre-origin
   frames). Appending stale pre-origin audio at the origin would delay all real audio.

3. **Preserve inter-source start offsets.** A source that genuinely starts *after* the origin (e.g.
   microphone vs. system audio) is padded with leading silence so the offset between sources is
   preserved.

4. **Advance by capture latency.** WASAPI timestamps the buffer read, which trails the true acoustic
   capture instant. Audio is advanced by the source's `IAudioClient.StreamLatency` so recorded sound
   lines up with video captured at the same wall-clock instant. (Some drivers report `0` here; the
   dominant sync mechanism is the back-pressure below.)

## Clean capture — `TimestampedWasapiCapture`

A custom WASAPI capture (rather than NAudio's `WasapiCapture`) so packets carry their QPC capture
timestamps for timeline alignment. Two things keep it dropout-free under load:

- **200 ms capture buffer** requested in `IAudioClient.Initialize`, polled on a **fixed 8 ms
  interval** at **`ThreadPriority.Highest`**. Every recording currently falls back to *software*
  H.264 encoding (the default hardware profile hits `MF_E_TRANSFORM_TYPE_NOT_SET` / `0xC00D6D60`),
  which is CPU-heavy; a small buffer on a normal-priority polling thread would get starved and
  overrun, dropping samples.
- **Mix-format standardization.** WASAPI's shared-mode mix format is a `WaveFormatExtensible`, which
  NAudio's sample-provider converters reject (`ArgumentException: Unsupported source encoding`). The
  capture initializes WASAPI with the native format but exposes `WaveFormatExtensible.ToStandardWaveFormat()`
  (identical byte layout, IEEE-float/PCM tag) to the NAudio pipeline. Without this, both audio
  sources fail to start silently and recordings have **no audio track**.

## Back-pressure — the key to A/V sync

This is the mechanism that actually keeps audio and video aligned end-to-end.

The audio source (`AudioCaptureService.ReadChunk`) pads silence on demand (`ReadFully`), so it will
*always* return a full chunk even when little real audio has been captured. Left unchecked, the
`MediaTranscoder` drains the audio stream **far faster than real time**, races the entire audio track
~1 s ahead, and captured sound then lands ~1 s **late** on playback.

The fix (`VideoRecordingService.HandleAudioRequestAsync`) is proper producer/consumer back-pressure:
**only hand the muxer a 20 ms chunk once that many real frames have actually been captured** across
all sources (`AudioCaptureService.AvailableFrames`, the minimum buffered duration over sources).

- Audio can never get ahead of real capture progress → **no racing / no delay**.
- We never read an empty buffer → **no silence-splicing crackle**.
- A 2 s wait cap prevents a stalled capture from hanging the transcode pull.

> **Do not** gate audio reads to `timeline.Elapsed` (the recording wall clock). An earlier version
> did, and it still read whenever the buffer was momentarily thin (WASAPI capture latency), splicing
> in silence and crackling. Gate on **captured-frame availability**, not the clock.

## Muxer PTS

Audio sample PTS is a monotonic counter derived from frames actually handed to the muxer
(`_audioFramesRead / SampleRate`), not from per-packet timestamps — this is what lets later packets be
appended contiguously without re-quantizing. Because the alignment above pins buffer position 0 to
the origin, and back-pressure pins the read rate to real capture, buffer position *X* both plays at
PTS *X* and was captured at real time `origin + X`, so audio and video stay locked together.

## Diagnostics

Per-recording tracing is written to `webcam-diagnostics.log` (packaged app:
`%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\TinyClips\`). Useful lines when
debugging sync/audio:

- `Audio capture loop started … latencyMs=…` — WASAPI stream latency and buffer/poll settings.
- `TimelineAlignedWaveProvider aligned: sourceOffsetMs=… latencyMs=… trimFrames/padFrames=…` — how
  the first packet landed on the origin.
- `First screen frame emitted: ptsMs=…` — where the video stream starts.
- `Audio muxer progress: requests=… nonSilentChunks=… framesRead=…` — verify `framesRead` tracks
  real elapsed time (≈ `SampleRate × seconds`); if it runs ahead, back-pressure isn't holding.

## See also

- [DPI & Coordinates on Windows](./dpi-and-coordinates.md)
- macOS equivalent: shared-clock capture in `mac/TinyClips/Capture/VideoRecorder.swift`
