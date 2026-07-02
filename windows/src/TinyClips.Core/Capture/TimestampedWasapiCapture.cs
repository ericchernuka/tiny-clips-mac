using System.Runtime.InteropServices;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace TinyClips.Core.Capture;

/// <summary>
/// Minimal WASAPI capture loop that preserves each packet's QPC timestamp. NAudio's
/// WasapiCapture event intentionally omits this timestamp, which makes independent
/// microphone and loopback streams impossible to align reliably.
/// </summary>
internal sealed class TimestampedWasapiCapture : IDisposable
{
    private const long ReferenceTimesPerSecond = TimeSpan.TicksPerSecond;
    private const long ReferenceTimesPerMillisecond = TimeSpan.TicksPerMillisecond;

    // Request a generous capture buffer so a busy CPU (e.g. software H.264 encoding) can stall
    // the polling thread for tens of milliseconds without WASAPI overrunning and dropping samples.
    private const long RequestedBufferDurationMs = 200;

    // Poll well inside the buffer window regardless of the negotiated buffer size.
    private const int PollIntervalMs = 8;

    private readonly MMDevice _device;
    private readonly AudioClient _audioClient;
    private readonly bool _isLoopback;
    private readonly WaveFormat _mixFormat;
    private Thread? _captureThread;
    private volatile bool _capturing;
    private bool _initialized;

    public TimestampedWasapiCapture(MMDevice device, bool isLoopback)
    {
        _device = device;
        _audioClient = device.AudioClient;
        _isLoopback = isLoopback;

        // WASAPI's shared-mode mix format is a WaveFormatExtensible. Initialize WASAPI with
        // that exact format, but expose a "standard" IEEE-float/PCM WaveFormat to the NAudio
        // pipeline. NAudio's sample-provider converters reject the Extensible encoding with
        // "Unsupported source encoding"; the byte layout is identical, so only the format tag
        // differs and the raw capture bytes reinterpret cleanly.
        _mixFormat = _audioClient.MixFormat;
        WaveFormat = _mixFormat is WaveFormatExtensible extensible
            ? extensible.ToStandardWaveFormat()
            : _mixFormat;
    }

    public WaveFormat WaveFormat { get; }

    public event Action<byte[], int, TimeSpan>? DataAvailable;

    public void Start()
    {
        if (_capturing)
        {
            throw new InvalidOperationException("Audio capture is already running.");
        }

        Initialize();
        _capturing = true;
        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = _isLoopback ? "TinyClips.SystemAudioCapture" : "TinyClips.MicrophoneCapture",
            Priority = ThreadPriority.Highest,
        };
        _captureThread.Start();
    }

    public void Stop()
    {
        _capturing = false;
        if (_captureThread is not null && _captureThread != Thread.CurrentThread)
        {
            _captureThread.Join(2000);
            _captureThread = null;
        }
    }

    private void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        var streamFlags = AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality;
        if (_isLoopback)
        {
            streamFlags |= AudioClientStreamFlags.Loopback;
        }

        var requestedDuration = RequestedBufferDurationMs * ReferenceTimesPerMillisecond;
        _audioClient.Initialize(
            AudioClientShareMode.Shared,
            streamFlags,
            requestedDuration,
            0,
            _mixFormat,
            Guid.Empty);
        _initialized = true;
    }

    private void CaptureLoop()
    {
        var packetCount = 0L;
        var nonSilentPackets = 0L;
        var loggedFirst = false;
        try
        {
            var captureClient = _audioClient.AudioCaptureClient;
            var bufferFrameCount = _audioClient.BufferSize;

            _audioClient.Start();
            WebcamDiagnostics.Log($"Audio capture loop started ({(_isLoopback ? "loopback" : "microphone")}): bufferFrames={bufferFrameCount} pollMs={PollIntervalMs}.");
            while (_capturing)
            {
                Thread.Sleep(PollIntervalMs);
                ReadAvailablePackets(captureClient, ref packetCount, ref nonSilentPackets, ref loggedFirst);
            }
        }
        catch (Exception ex)
        {
            // Capture sources are best-effort. The other source and video remain usable.
            WebcamDiagnostics.Log($"Audio capture loop crashed ({(_isLoopback ? "loopback" : "microphone")}): {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            WebcamDiagnostics.Log($"Audio capture loop ended ({(_isLoopback ? "loopback" : "microphone")}): packets={packetCount} nonSilentPackets={nonSilentPackets}.");
            try
            {
                _audioClient.Stop();
            }
            catch
            {
                // Ignore teardown errors.
            }

            _capturing = false;
        }
    }

    private void ReadAvailablePackets(AudioCaptureClient captureClient, ref long packetCount, ref long nonSilentPackets, ref bool loggedFirst)
    {
        var packetFrames = captureClient.GetNextPacketSize();
        while (_capturing && packetFrames > 0)
        {
            var dataPointer = captureClient.GetBuffer(
                out var framesAvailable,
                out var flags,
                out _,
                out var qpcPosition);
            try
            {
                var byteCount = checked(framesAvailable * WaveFormat.BlockAlign);
                var data = new byte[byteCount];
                var silent = (flags & AudioClientBufferFlags.Silent) != 0;
                if (!silent)
                {
                    Marshal.Copy(dataPointer, data, 0, byteCount);
                }

                packetCount++;
                if (!silent)
                {
                    nonSilentPackets++;
                }

                if (!loggedFirst)
                {
                    loggedFirst = true;
                    WebcamDiagnostics.Log($"Audio first packet ({(_isLoopback ? "loopback" : "microphone")}): frames={framesAvailable} bytes={byteCount} silent={silent} qpc={qpcPosition}.");
                }

                // WASAPI reports the QPC position in 100-nanosecond units and it refers
                // to the first audio frame in this packet. Fall back to an arrival-time
                // estimate only when the audio driver explicitly marks that timestamp invalid.
                var sourceTimestamp = (flags & AudioClientBufferFlags.TimestampError) == 0 && qpcPosition > 0
                    ? TimeSpan.FromTicks(qpcPosition)
                    : Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()) -
                        TimeSpan.FromSeconds(framesAvailable / (double)WaveFormat.SampleRate);
                DataAvailable?.Invoke(data, byteCount, sourceTimestamp);
            }
            finally
            {
                captureClient.ReleaseBuffer(framesAvailable);
            }

            packetFrames = captureClient.GetNextPacketSize();
        }
    }

    public void Dispose()
    {
        Stop();
        _audioClient.Dispose();
        _device.Dispose();
    }
}
