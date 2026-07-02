using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace TinyClips.Core.Capture;

public sealed class WebcamCaptureService : IWebcamCaptureService
{
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly object _latestFrameGate = new();

    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private int _nextMediaCaptureId;
    private int _activeMediaCaptureId;
    private bool _stopRequested;
    private bool _isDisposed;

    private WebcamFrame? _latestFrame;
    private long _framesArrived;
    private int _firstFrameLogged;
    private int _firstCacheLogged;
    private int _frameErrorLogged;

    public bool IsRunning { get; private set; }

    public event EventHandler<WebcamCaptureFailedEventArgs>? CaptureFailed;

    public async Task StartAsync(string? deviceId, BitmapSize bitmapSize, CancellationToken cancellationToken = default)
    {
        if (bitmapSize.Width == 0 || bitmapSize.Height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitmapSize), "Bitmap size must be greater than 0.");
        }

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (IsRunning)
            {
                throw new InvalidOperationException("Webcam capture is already running.");
            }

            _stopRequested = false;
            Interlocked.Exchange(ref _framesArrived, 0);
            Interlocked.Exchange(ref _firstFrameLogged, 0);
            Interlocked.Exchange(ref _firstCacheLogged, 0);
            Interlocked.Exchange(ref _frameErrorLogged, 0);
            lock (_latestFrameGate)
            {
                _latestFrame = null;
            }

            WebcamDiagnostics.Log($"WebcamCaptureService.StartAsync deviceId='{(string.IsNullOrWhiteSpace(deviceId) ? "(default)" : deviceId)}' size={bitmapSize.Width}x{bitmapSize.Height}");

            var mediaCapture = new MediaCapture();
            var captureId = Interlocked.Increment(ref _nextMediaCaptureId);
            mediaCapture.Failed += (sender, args) => OnMediaCaptureFailed(sender, args, captureId);

            try
            {
                var settings = new MediaCaptureInitializationSettings
                {
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    VideoDeviceId = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : deviceId,
                };

                await mediaCapture.InitializeAsync(settings).AsTask(cancellationToken).ConfigureAwait(false);
                WebcamDiagnostics.Log("InitializeAsync succeeded.");

                if (_stopRequested)
                {
                    mediaCapture.Dispose();
                    return;
                }

                var source = SelectPreferredSource(mediaCapture.FrameSources)
                    ?? throw new InvalidOperationException("No color webcam frame source was available.");
                WebcamDiagnostics.Log($"Selected frame source kind={source.Info.SourceKind} streamType={source.Info.MediaStreamType} id={source.Info.Id}");

                var frameReader = await mediaCapture
                    .CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8, bitmapSize)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);

                frameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
                frameReader.FrameArrived += OnFrameArrived;

                var startStatus = await frameReader.StartAsync().AsTask(cancellationToken).ConfigureAwait(false);
                WebcamDiagnostics.Log($"frameReader.StartAsync status={startStatus}");
                if (startStatus != MediaFrameReaderStartStatus.Success)
                {
                    frameReader.FrameArrived -= OnFrameArrived;
                    frameReader.Dispose();
                    throw new InvalidOperationException($"Failed to start webcam frame reader: {startStatus}.");
                }

                if (_stopRequested)
                {
                    frameReader.FrameArrived -= OnFrameArrived;
                    await frameReader.StopAsync().AsTask().ConfigureAwait(false);
                    frameReader.Dispose();
                    mediaCapture.Dispose();
                    return;
                }

                _mediaCapture = mediaCapture;
                _frameReader = frameReader;
                Volatile.Write(ref _activeMediaCaptureId, captureId);
                IsRunning = true;
                WebcamDiagnostics.Log("Webcam capture is now running (IsRunning=true).");
            }
            catch (Exception ex)
            {
                WebcamDiagnostics.Log($"StartAsync FAILED: 0x{(uint)ex.HResult:X8} {ex.GetType().Name}: {ex.Message}");
                mediaCapture.Dispose();
                throw;
            }
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task StopAsync()
    {
        _stopRequested = true;

        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var frameReader = _frameReader;
            var mediaCapture = _mediaCapture;

            _frameReader = null;
            _mediaCapture = null;
            Volatile.Write(ref _activeMediaCaptureId, 0);
            IsRunning = false;

            if (mediaCapture is not null)
            {
                WebcamDiagnostics.Log($"WebcamCaptureService.StopAsync — total frames arrived={Interlocked.Read(ref _framesArrived)}");
            }

            if (frameReader is not null)
            {
                frameReader.FrameArrived -= OnFrameArrived;
            }

            if (frameReader is not null)
            {
                try
                {
                    await frameReader.StopAsync().AsTask().ConfigureAwait(false);
                }
                catch
                {
                    // Ignore stop errors while tearing down.
                }
                finally
                {
                    frameReader.Dispose();
                }
            }

            mediaCapture?.Dispose();
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public bool TryGetLatestFrame(out WebcamFrame? frame)
    {
        lock (_latestFrameGate)
        {
            frame = _latestFrame;
            return frame is not null;
        }
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            using var frameReference = sender.TryAcquireLatestFrame();
            var softwareBitmap = frameReference?.VideoMediaFrame?.SoftwareBitmap;
            if (softwareBitmap is null)
            {
                return;
            }

            var timestamp = frameReference!.SystemRelativeTime ?? TimeSpan.Zero;
            Interlocked.Increment(ref _framesArrived);
            if (Interlocked.Exchange(ref _firstFrameLogged, 1) == 0)
            {
                WebcamDiagnostics.Log($"First frame arrived: {softwareBitmap.PixelWidth}x{softwareBitmap.PixelHeight} format={softwareBitmap.BitmapPixelFormat} alpha={softwareBitmap.BitmapAlphaMode}");
            }

            // The overlay compositor blends BGR using its own shape mask and ignores the
            // webcam's source alpha, so when the camera already delivers Bgra8 we copy the
            // raw bytes directly — avoiding a per-frame SoftwareBitmap.Convert that can fail
            // in the packaged runtime. Only non-Bgra8 formats (e.g. Nv12/Yuy2) need a convert.
            SoftwareBitmap? convertedBitmap = null;
            var preparedBitmap = softwareBitmap;
            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
            {
                convertedBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
                preparedBitmap = convertedBitmap;
            }

            try
            {
                // Keep the source's absolute system-relative timestamp. The recorder
                // normalizes it against the same QPC origin used by screen and audio.
                var frame = CopyFrame(preparedBitmap, timestamp);
                lock (_latestFrameGate)
                {
                    _latestFrame = frame;
                }

                if (Interlocked.Exchange(ref _firstCacheLogged, 1) == 0)
                {
                    WebcamDiagnostics.Log($"First webcam frame cached for compositing: {frame.Width}x{frame.Height} (converted={(convertedBitmap is not null)})");
                }
            }
            finally
            {
                convertedBitmap?.Dispose();
            }
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _frameErrorLogged, 1) == 0)
            {
                WebcamDiagnostics.Log($"OnFrameArrived FAILED (frame not cached): 0x{(uint)ex.HResult:X8} {ex.GetType().Name}: {ex.Message}");
            }
            // A single failed frame should not stop webcam capture.
        }
    }

    private async void OnMediaCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs args, int captureId)
    {
        if (captureId != Volatile.Read(ref _activeMediaCaptureId))
        {
            return;
        }

        WebcamDiagnostics.Log($"MediaCapture.Failed fired: code={args.Code} message='{args.Message}' (frames so far={Interlocked.Read(ref _framesArrived)})");
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // Failure handling should not throw back into MediaCapture internals.
        }

        CaptureFailed?.Invoke(this, new WebcamCaptureFailedEventArgs(args.Code, args.Message));
    }

    private static MediaFrameSource? SelectPreferredSource(IReadOnlyDictionary<string, MediaFrameSource> frameSources)
    {
        return frameSources.Values
            .Where(source => source.Info.SourceKind == MediaFrameSourceKind.Color)
            .OrderBy(source => source.Info.MediaStreamType == MediaStreamType.VideoPreview ? 0 :
                source.Info.MediaStreamType == MediaStreamType.VideoRecord ? 1 : 2)
            .ThenBy(source => source.Info.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static WebcamFrame CopyFrame(SoftwareBitmap bitmap, TimeSpan timestamp)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;

        // Read the frame's pixel bytes through a fully WinRT-projected path
        // (CopyToBuffer + DataReader) rather than the IMemoryBufferByteAccess COM-interop
        // interface. Under C#/WinRT that interface fails to QueryInterface (E_NOINTERFACE /
        // "Specified cast is not valid"), so every frame was being dropped before compositing.
        var pixelBuffer = new Windows.Storage.Streams.Buffer((uint)(width * height * 4));
        bitmap.CopyToBuffer(pixelBuffer);

        var raw = new byte[pixelBuffer.Length];
        using (var reader = DataReader.FromBuffer(pixelBuffer))
        {
            reader.ReadBytes(raw);
        }

        var packedStride = width * 4;
        var totalLength = raw.Length;

        // CopyToBuffer packs Bgra8 rows with no inter-row padding (stride == width*4) for the
        // formats this service requests, so the buffer is already the layout WebcamFrame expects.
        if (totalLength == packedStride * height)
        {
            return new WebcamFrame(raw, width, height, timestamp);
        }

        // Defensive fallback: de-stride if a driver ever reports padded rows.
        var stride = height > 0 ? totalLength / height : packedStride;
        if (stride < packedStride || height <= 0)
        {
            return new WebcamFrame(raw, width, height, timestamp);
        }

        var destination = new byte[packedStride * height];
        for (var row = 0; row < height; row++)
        {
            Array.Copy(raw, row * stride, destination, row * packedStride, packedStride);
        }

        return new WebcamFrame(destination, width, height, timestamp);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(WebcamCaptureService));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await StopAsync().ConfigureAwait(false);
        _stateGate.Dispose();
    }
}
