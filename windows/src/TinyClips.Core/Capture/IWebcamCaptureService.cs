using Windows.Graphics.Imaging;

namespace TinyClips.Core.Capture;

/// <summary>
/// A webcam frame in tightly-packed BGRA8 pixels with its system-relative source timestamp.
/// </summary>
public sealed class WebcamFrame
{
    public WebcamFrame(ReadOnlyMemory<byte> bgraPixels, int width, int height, TimeSpan timestamp)
    {
        BgraPixels = bgraPixels;
        Width = width;
        Height = height;
        Timestamp = timestamp;
    }

    public ReadOnlyMemory<byte> BgraPixels { get; }

    public int Width { get; }

    public int Height { get; }

    public TimeSpan Timestamp { get; }
}

/// <summary>
/// Provides lifecycle management and frame access for webcam capture.
/// </summary>
public interface IWebcamCaptureService : IAsyncDisposable
{
    bool IsRunning { get; }

    event EventHandler<WebcamCaptureFailedEventArgs>? CaptureFailed;

    Task StartAsync(string? deviceId, BitmapSize bitmapSize, CancellationToken cancellationToken = default);

    Task StopAsync();

    bool TryGetLatestFrame(out WebcamFrame? frame);
}

/// <summary>
/// Failure details raised from the webcam capture pipeline.
/// </summary>
public sealed class WebcamCaptureFailedEventArgs : EventArgs
{
    public WebcamCaptureFailedEventArgs(uint code, string message)
    {
        Code = code;
        Message = message;
    }

    public uint Code { get; }

    public string Message { get; }
}
