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

    private readonly MMDevice _device;
    private readonly AudioClient _audioClient;
    private readonly bool _isLoopback;
    private Thread? _captureThread;
    private volatile bool _capturing;
    private bool _initialized;

    public TimestampedWasapiCapture(MMDevice device, bool isLoopback)
    {
        _device = device;
        _audioClient = device.AudioClient;
        _isLoopback = isLoopback;
        WaveFormat = _audioClient.MixFormat;
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

        var requestedDuration = 20 * ReferenceTimesPerMillisecond;
        _audioClient.Initialize(
            AudioClientShareMode.Shared,
            streamFlags,
            requestedDuration,
            0,
            WaveFormat,
            Guid.Empty);
        _initialized = true;
    }

    private void CaptureLoop()
    {
        try
        {
            var captureClient = _audioClient.AudioCaptureClient;
            var bufferFrameCount = _audioClient.BufferSize;
            var actualDuration = ReferenceTimesPerSecond * bufferFrameCount / WaveFormat.SampleRate;
            var sleepMilliseconds = Math.Max(1, (int)(actualDuration / ReferenceTimesPerMillisecond / 2));

            _audioClient.Start();
            while (_capturing)
            {
                Thread.Sleep(sleepMilliseconds);
                ReadAvailablePackets(captureClient);
            }
        }
        catch
        {
            // Capture sources are best-effort. The other source and video remain usable.
        }
        finally
        {
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

    private void ReadAvailablePackets(AudioCaptureClient captureClient)
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
                if ((flags & AudioClientBufferFlags.Silent) == 0)
                {
                    Marshal.Copy(dataPointer, data, 0, byteCount);
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
