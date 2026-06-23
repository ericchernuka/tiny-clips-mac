using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace TinyClips.Core.Services;

/// <summary>A capturable webcam device for settings selection.</summary>
public sealed record WebcamDeviceInfo(string Id, string Name);

/// <summary>Enumerates available webcam devices for settings UI consumption.</summary>
public interface IWebcamDeviceEnumerator
{
    /// <summary>
    /// Returns available video capture devices in stable display order.
    /// </summary>
    Task<IReadOnlyList<WebcamDeviceInfo>> GetWebcamDevicesAsync();
}

/// <inheritdoc />
public sealed class WebcamDeviceEnumerator : IWebcamDeviceEnumerator
{
    public async Task<IReadOnlyList<WebcamDeviceInfo>> GetWebcamDevicesAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetVideoCaptureSelector());

            return devices
                .Select(device => new WebcamDeviceInfo(
                    device.Id,
                    string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name))
                .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            // Enumeration can fail on machines where camera access is denied/unavailable.
            return Array.Empty<WebcamDeviceInfo>();
        }
    }
}
