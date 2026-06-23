namespace TinyClips.Core.Models;

/// <summary>
/// H.264 encoder profile used when transcoding recorded video.
/// </summary>
public enum VideoEncoderProfile
{
    /// <summary>
    /// High profile (default). Enables B-frames and CABAC for the best quality
    /// and smallest files at a given bitrate.
    /// </summary>
    High = 0,

    /// <summary>
    /// Constrained Baseline profile. Disables B-frames for maximum playback
    /// compatibility at the cost of larger files / lower quality.
    /// </summary>
    Baseline = 1,
}
