using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace TinyClips.Core.Capture;

public sealed class WebcamCaptureService : IWebcamCaptureService
{
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly object _latestFrameGate = new();

    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private MediaCaptureFailedEventHandler? _failedHandler;
    private bool _stopRequested;
    private bool _isDisposed;

    private WebcamFrame? _latestFrame;
    private TimeSpan? _baseTimestamp;

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
            _baseTimestamp = null;
            lock (_latestFrameGate)
            {
                _latestFrame = null;
            }

            var mediaCapture = new MediaCapture();
            MediaCaptureFailedEventHandler failedHandler = OnMediaCaptureFailed;
            mediaCapture.Failed += failedHandler;

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

                if (_stopRequested)
                {
                    mediaCapture.Failed -= failedHandler;
                    mediaCapture.Dispose();
                    return;
                }

                var source = SelectPreferredSource(mediaCapture.FrameSources)
                    ?? throw new InvalidOperationException("No color webcam frame source was available.");

                var frameReader = await mediaCapture
                    .CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8, bitmapSize)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);

                frameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
                frameReader.FrameArrived += OnFrameArrived;

                var startStatus = await frameReader.StartAsync().AsTask(cancellationToken).ConfigureAwait(false);
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
                    mediaCapture.Failed -= failedHandler;
                    mediaCapture.Dispose();
                    return;
                }

                _mediaCapture = mediaCapture;
                _frameReader = frameReader;
                _failedHandler = failedHandler;
                IsRunning = true;
            }
            catch
            {
                mediaCapture.Failed -= failedHandler;
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
            var failedHandler = _failedHandler;

            _frameReader = null;
            _mediaCapture = null;
            _failedHandler = null;
            IsRunning = false;

            if (frameReader is not null)
            {
                frameReader.FrameArrived -= OnFrameArrived;
            }

            if (mediaCapture is not null && failedHandler is not null)
            {
                mediaCapture.Failed -= failedHandler;
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
            _baseTimestamp = null;
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
            if (_baseTimestamp is null)
            {
                _baseTimestamp = timestamp;
            }

            SoftwareBitmap? convertedBitmap = null;
            var preparedBitmap = softwareBitmap;
            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                convertedBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                preparedBitmap = convertedBitmap;
            }

            try
            {
                var frame = CopyFrame(preparedBitmap, timestamp - _baseTimestamp.Value);
                lock (_latestFrameGate)
                {
                    _latestFrame = frame;
                }
            }
            finally
            {
                convertedBitmap?.Dispose();
            }
        }
        catch
        {
            // A single failed frame should not stop webcam capture.
        }
    }

    private async void OnMediaCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs args)
    {
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

    private static unsafe WebcamFrame CopyFrame(SoftwareBitmap bitmap, TimeSpan timestamp)
    {
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        ((IMemoryBufferByteAccess)reference).GetBuffer(out var sourceBytes, out _);

        var plane = buffer.GetPlaneDescription(0);
        var width = plane.Width;
        var height = plane.Height;
        var destination = new byte[width * height * 4];

        for (var row = 0; row < height; row++)
        {
            var sourceOffset = plane.StartIndex + (row * plane.Stride);
            var destinationOffset = row * width * 4;
            Marshal.Copy((nint)(sourceBytes + sourceOffset), destination, destinationOffset, width * 4);
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

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-8650-BC5D5A8F715A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
