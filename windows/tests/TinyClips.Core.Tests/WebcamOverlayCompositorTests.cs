using TinyClips.Core.Capture;
using TinyClips.Core.Models;

namespace TinyClips.Core.Tests;

public sealed class WebcamOverlayCompositorTests
{
    [Fact]
    public void Draw_TreatsWebcamPixelsAsOpaque_WhenSourceAlphaIsZero()
    {
        var compositor = new WebcamOverlayCompositor(
            WebcamCornerPosition.TopLeft,
            WebcamSizePreset.Small,
            WebcamShape.Rectangle,
            cornerRadius: null);
        var destination = new byte[100 * 100 * 4];
        var webcam = new byte[10 * 10 * 4];

        for (var i = 0; i < webcam.Length; i += 4)
        {
            webcam[i] = 0;
            webcam[i + 1] = 0;
            webcam[i + 2] = 255;
            webcam[i + 3] = 0;
        }

        compositor.Draw(destination, 100, 100, new WebcamFrame(webcam, 10, 10, TimeSpan.Zero));

        Assert.Contains(destination.Chunk(4), pixel => pixel[2] == 255);
    }
}
