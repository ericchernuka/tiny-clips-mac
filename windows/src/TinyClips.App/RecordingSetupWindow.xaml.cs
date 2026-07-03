using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TinyClips.Core.Capture;
using TinyClips.Core.Models;
using TinyClips.Core.Services;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace TinyClips.App;

public sealed record RecordingSetupResult(
    bool RecordSystemAudio,
    bool RecordMicrophone,
    string SelectedMicrophoneId,
    bool WebcamEnabled,
    string SelectedWebcamId,
    WebcamShape WebcamShape,
    WebcamSizePreset WebcamSizePreset,
    WebcamCornerPosition WebcamCornerPosition,
    double? WebcamCornerRadius,
    bool ShowMouseClicks);

/// <summary>
/// Pre-recording setup panel shown after target selection and before countdown.
/// </summary>
public sealed partial class RecordingSetupWindow : Window
{
    private const int TopOffsetDip = 24;
    private const int RegionOutsideOffsetDip = 12;
    private const uint WdaExcludeFromCapture = 0x11;

    private const string MicGlyph = "\uE720";
    private const string SystemAudioOnGlyph = "\uE767";
    private const string SystemAudioOffGlyph = "\uE74F";
    private static readonly double[] WebcamCornerRadiusOptions = { -1d, 8d, 12d, 16d, 24d, 32d, 48d };

    private readonly TaskCompletionSource<RecordingSetupResult?> _result = new();
    private readonly CaptureType _captureType;
    private readonly IAudioDeviceService _audioDevices;
    private readonly IWebcamDeviceEnumerator _webcamDevices;
    private readonly IMediaDevicePermissionService _mediaPermissions;
    private readonly ObservableCollection<AudioInputDevice> _microphones = new();
    private readonly ObservableCollection<WebcamDeviceInfo> _webcams = new();

    private bool _completed;
    private bool _closed;
    private bool _suppressEvents;
    private bool _microphonesLoading;
    private bool _webcamsLoading;
    private bool _recordSystemAudio;
    private bool _recordMicrophone;
    private string _selectedMicrophoneId;
    private bool _webcamEnabled;
    private string _selectedWebcamId;
    private WebcamShape _webcamShape;
    private WebcamSizePreset _webcamSizePreset;
    private WebcamCornerPosition _webcamCornerPosition;
    private double _webcamCornerRadius;
    private bool _showMouseClicks;

    private bool _dragging;
    private POINT _dragCursorStart;
    private PointInt32 _dragWindowStart;

    private RecordingSetupWindow(
        CaptureType captureType,
        ICaptureSettings settings,
        IAudioDeviceService audioDevices,
        IWebcamDeviceEnumerator webcamDevices,
        IMediaDevicePermissionService mediaPermissions)
    {
        InitializeComponent();

        _captureType = captureType;
        _audioDevices = audioDevices;
        _webcamDevices = webcamDevices;
        _mediaPermissions = mediaPermissions;
        _recordSystemAudio = settings.RecordAudio;
        _recordMicrophone = settings.RecordMicrophone;
        _selectedMicrophoneId = settings.SelectedMicrophoneId ?? string.Empty;
        _webcamEnabled = settings.WebcamEnabled;
        _selectedWebcamId = settings.SelectedWebcamId ?? string.Empty;
        _webcamShape = settings.WebcamShape;
        _webcamSizePreset = settings.WebcamSizePreset;
        _webcamCornerPosition = settings.WebcamCornerPosition;
        _webcamCornerRadius = settings.WebcamCornerRadius ?? -1;
        _showMouseClicks = settings.ShouldShowMouseClickVisuals(captureType);

        _microphones.Add(new AudioInputDevice(string.Empty, "System default"));
        _webcams.Add(new WebcamDeviceInfo(string.Empty, "System default"));

        ConfigurePresenter();
        ConfigureForCaptureType();
        RebuildMicrophoneFlyout(loading: false);
        RebuildWebcamSettingsFlyout(loading: false);
        UpdateVisuals();

        Closed += OnClosed;
    }

    public static Task<RecordingSetupResult?> RunAsync(
        CaptureType captureType,
        ICaptureSettings settings,
        IAudioDeviceService audioDevices,
        IWebcamDeviceEnumerator webcamDevices,
        IMediaDevicePermissionService mediaPermissions,
        MonitorInfo? monitor,
        PixelRect? regionInVirtualDesktop)
    {
        var window = new RecordingSetupWindow(
            captureType,
            settings,
            audioDevices,
            webcamDevices,
            mediaPermissions);
        window.ShowNear(monitor, regionInVirtualDesktop);
        if (captureType == CaptureType.Video)
        {
            _ = window.LoadMicrophonesAsync();
            _ = window.LoadWebcamsAsync();
        }

        return window._result.Task;
    }

    private void ConfigureForCaptureType()
    {
        if (_captureType == CaptureType.Gif)
        {
            SystemAudioToggle.Visibility = Visibility.Collapsed;
            MicrophoneToggle.Visibility = Visibility.Collapsed;
            MicrophoneDeviceButton.Visibility = Visibility.Collapsed;
            WebcamToggle.Visibility = Visibility.Collapsed;
            WebcamSettingsButton.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadMicrophonesAsync()
    {
        SetMicrophoneLoading(true);
        try
        {
            var microphones = await Task.Run(() => _audioDevices.GetMicrophones());
            if (_closed)
            {
                return;
            }

            ApplyMicrophones(microphones);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Microphone enumeration failed: {ex}");
            if (_closed)
            {
                return;
            }

            ApplyMicrophones(Array.Empty<AudioInputDevice>());
        }
        finally
        {
            SetMicrophoneLoading(false);
        }
    }

    private void ApplyMicrophones(IReadOnlyList<AudioInputDevice> microphones)
    {
        _suppressEvents = true;
        try
        {
            _microphones.Clear();
            if (microphones.Count == 0)
            {
                _microphones.Add(new AudioInputDevice(string.Empty, "System default"));
            }
            else
            {
                foreach (var microphone in microphones)
                {
                    _microphones.Add(microphone);
                }
            }

            var selected = _microphones.FirstOrDefault(m => m.Id == _selectedMicrophoneId) ?? _microphones[0];
            _selectedMicrophoneId = selected.Id;
        }
        finally
        {
            _suppressEvents = false;
            RebuildMicrophoneFlyout(loading: false);
            UpdateMicrophonePickerEnabled();
        }
    }

    private void SetMicrophoneLoading(bool loading)
    {
        if (_closed)
        {
            return;
        }

        _microphonesLoading = loading;
        RebuildMicrophoneFlyout(loading);
        UpdateMicrophonePickerEnabled();
    }

    private async Task LoadWebcamsAsync()
    {
        SetWebcamLoading(true);
        try
        {
            var webcams = await _webcamDevices.GetWebcamDevicesAsync();
            if (_closed)
            {
                return;
            }

            ApplyWebcams(webcams);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Webcam enumeration failed: {ex}");
            if (_closed)
            {
                return;
            }

            ApplyWebcams(Array.Empty<WebcamDeviceInfo>());
        }
        finally
        {
            SetWebcamLoading(false);
        }
    }

    private void ApplyWebcams(IReadOnlyList<WebcamDeviceInfo> webcams)
    {
        _suppressEvents = true;
        try
        {
            _webcams.Clear();
            _webcams.Add(new WebcamDeviceInfo(string.Empty, "System default"));

            foreach (var webcam in webcams)
            {
                _webcams.Add(webcam);
            }

            var selected = _webcams.FirstOrDefault(w => w.Id == _selectedWebcamId) ?? _webcams[0];
            _selectedWebcamId = selected.Id;
        }
        finally
        {
            _suppressEvents = false;
            RebuildWebcamSettingsFlyout(loading: false);
            UpdateWebcamSettingsEnabled();
        }
    }

    private void SetWebcamLoading(bool loading)
    {
        if (_closed)
        {
            return;
        }

        _webcamsLoading = loading;
        RebuildWebcamSettingsFlyout(loading);
        UpdateWebcamSettingsEnabled();
    }

    private void ShowNear(MonitorInfo? monitor, PixelRect? regionInVirtualDesktop)
    {
        PositionNearMonitorWorkArea(monitor, regionInVirtualDesktop);
        Activate();
        RootGrid.Focus(FocusState.Programmatic);

        var hwnd = WindowNative.GetWindowHandle(this);
        SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
    }

    private void PositionNearMonitorWorkArea(MonitorInfo? monitor, PixelRect? regionInVirtualDesktop)
    {
        var scale = GetScale();
        RootGrid.UpdateLayout();
        RootGrid.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));

        var width = (int)Math.Ceiling(RootGrid.DesiredSize.Width * scale) + 2;
        var height = (int)Math.Ceiling(RootGrid.DesiredSize.Height * scale) + 2;
        var topOffset = (int)Math.Round(TopOffsetDip * scale);
        var regionOutsideOffset = (int)Math.Round(RegionOutsideOffsetDip * scale);

        AppWindow.Resize(new SizeInt32(width, height));

        if (GetWorkArea(monitor) is not { } work)
        {
            return;
        }

        var x = work.X + Math.Max(0, (work.Width - width) / 2);
        var y = work.Y + topOffset;

        if (regionInVirtualDesktop is { Width: > 0, Height: > 0 } region)
        {
            x = region.X + Math.Max(0, (region.Width - width) / 2);
            var preferredAbove = region.Y - height - regionOutsideOffset;
            var preferredBelow = region.Y + region.Height + regionOutsideOffset;
            if (preferredAbove >= work.Y)
            {
                y = preferredAbove;
            }
            else if (preferredBelow <= work.Y + Math.Max(0, work.Height - height))
            {
                y = preferredBelow;
            }
            else
            {
                y = region.Y + topOffset;
            }
        }

        x = Math.Clamp(x, work.X, work.X + Math.Max(0, work.Width - width));
        y = Math.Clamp(y, work.Y, work.Y + Math.Max(0, work.Height - height));
        AppWindow.Move(new PointInt32(x, y));
    }

    private static RectInt32? GetWorkArea(MonitorInfo? monitor)
    {
        if (monitor is { WorkAreaWidth: > 0, WorkAreaHeight: > 0 })
        {
            return new RectInt32(monitor.WorkAreaX, monitor.WorkAreaY, monitor.WorkAreaWidth, monitor.WorkAreaHeight);
        }

        return DisplayArea.Primary?.WorkArea;
    }

    private double GetScale()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        return dpi <= 0 ? 1.0 : dpi / 96.0;
    }

    private void ConfigurePresenter()
    {
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
    }

    private void OnSystemAudioToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _recordSystemAudio = SystemAudioToggle.IsChecked == true;
        UpdateSystemAudioVisual();
    }

    private async void OnMicrophoneToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        if (MicrophoneToggle.IsChecked == true && !_recordMicrophone)
        {
            MicrophoneToggle.IsEnabled = false;
            _recordMicrophone = await _mediaPermissions.RequestMicrophoneAccessAsync();
            if (_closed)
            {
                return;
            }

            MicrophoneToggle.IsEnabled = true;
            SetMediaToggleStates();
        }
        else
        {
            _recordMicrophone = MicrophoneToggle.IsChecked == true;
        }

        UpdateMicrophoneVisual();
        UpdateMicrophonePickerEnabled();
    }

    private async void OnWebcamToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        if (WebcamToggle.IsChecked == true && !_webcamEnabled)
        {
            WebcamToggle.IsEnabled = false;
            MicrophoneToggle.IsEnabled = false;

            var isCameraAllowed = await _mediaPermissions.RequestCameraAccessAsync();
            if (_closed)
            {
                return;
            }

            var isMicrophoneAllowed = await _mediaPermissions.RequestMicrophoneAccessAsync();
            if (_closed)
            {
                return;
            }

            _webcamEnabled = isCameraAllowed;
            _recordMicrophone = isMicrophoneAllowed;
            SetMediaToggleStates();

            WebcamToggle.IsEnabled = true;
            MicrophoneToggle.IsEnabled = true;
        }
        else
        {
            _webcamEnabled = WebcamToggle.IsChecked == true;
        }

        UpdateWebcamVisual();
        UpdateWebcamSettingsEnabled();
        UpdateMicrophoneVisual();
        UpdateMicrophonePickerEnabled();
    }

    private void SetMediaToggleStates()
    {
        _suppressEvents = true;
        try
        {
            WebcamToggle.IsChecked = _webcamEnabled;
            MicrophoneToggle.IsChecked = _recordMicrophone;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void OnMouseClicksToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _showMouseClicks = MouseClicksToggle.IsChecked == true;
        UpdateMouseClicksVisual();
    }

    private void SelectMicrophone(AudioInputDevice microphone)
    {
        if (_suppressEvents)
        {
            return;
        }

        _selectedMicrophoneId = microphone.Id;
        RebuildMicrophoneFlyout(loading: false);
    }

    private void SelectWebcam(WebcamDeviceInfo webcam)
    {
        if (_suppressEvents)
        {
            return;
        }

        _selectedWebcamId = webcam.Id;
        RebuildWebcamSettingsFlyout(loading: false);
        UpdateWebcamSettingsSummary();
    }

    private void SelectWebcamShape(WebcamShape shape)
    {
        if (_suppressEvents)
        {
            return;
        }

        _webcamShape = shape;
        RebuildWebcamSettingsFlyout(loading: false);
        UpdateWebcamSettingsSummary();
    }

    private void SelectWebcamCorner(WebcamCornerPosition corner)
    {
        if (_suppressEvents)
        {
            return;
        }

        _webcamCornerPosition = corner;
        RebuildWebcamSettingsFlyout(loading: false);
        UpdateWebcamSettingsSummary();
    }

    private void SelectWebcamSize(WebcamSizePreset size)
    {
        if (_suppressEvents)
        {
            return;
        }

        _webcamSizePreset = size;
        RebuildWebcamSettingsFlyout(loading: false);
        UpdateWebcamSettingsSummary();
    }

    private void SelectWebcamCornerRadius(double radius)
    {
        if (_suppressEvents)
        {
            return;
        }

        _webcamCornerRadius = radius;
        RebuildWebcamSettingsFlyout(loading: false);
        UpdateWebcamSettingsSummary();
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        Complete(new RecordingSetupResult(
            _captureType == CaptureType.Video && _recordSystemAudio,
            _captureType == CaptureType.Video && _recordMicrophone,
            _selectedMicrophoneId,
            _captureType == CaptureType.Video && _webcamEnabled,
            _selectedWebcamId,
            _webcamShape,
            _webcamSizePreset,
            _webcamCornerPosition,
            _webcamCornerRadius < 0 ? null : _webcamCornerRadius,
            _showMouseClicks));
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Complete(null);

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                OnStart(sender, e);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                Complete(null);
                e.Handled = true;
                break;
        }
    }

    private void Complete(RecordingSetupResult? result)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _result.TrySetResult(result);
        ClosePanel();
    }

    private void ClosePanel()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        if (!_completed)
        {
            _completed = true;
            _result.TrySetResult(null);
        }
    }

    private void UpdateVisuals()
    {
        _suppressEvents = true;
        try
        {
            SystemAudioToggle.IsChecked = _recordSystemAudio;
            MicrophoneToggle.IsChecked = _recordMicrophone;
            WebcamToggle.IsChecked = _webcamEnabled;
            MouseClicksToggle.IsChecked = _showMouseClicks;
        }
        finally
        {
            _suppressEvents = false;
        }

        UpdateSystemAudioVisual();
        UpdateMicrophoneVisual();
        UpdateWebcamVisual();
        UpdateMouseClicksVisual();
        UpdateMicrophonePickerEnabled();
        UpdateWebcamSettingsEnabled();
        RebuildWebcamSettingsFlyout(_webcamsLoading);
        UpdateWebcamSettingsSummary();
    }

    private void UpdateSystemAudioVisual()
    {
        SystemAudioIcon.Glyph = _recordSystemAudio ? SystemAudioOnGlyph : SystemAudioOffGlyph;
        var state = _recordSystemAudio ? "On" : "Off";
        ToolTipService.SetToolTip(SystemAudioToggle, $"System audio: {state}");
        AutomationProperties.SetName(SystemAudioToggle, $"System audio {state}");
    }

    private void UpdateMicrophoneVisual()
    {
        MicrophoneIcon.Glyph = MicGlyph;
        MicrophoneSlash.Visibility = _recordMicrophone ? Visibility.Collapsed : Visibility.Visible;
        var state = _recordMicrophone ? "On" : "Off";
        ToolTipService.SetToolTip(MicrophoneToggle, $"Microphone: {state}");
        AutomationProperties.SetName(MicrophoneToggle, $"Microphone {state}");
    }

    private void UpdateWebcamVisual()
    {
        WebcamSlash.Visibility = _webcamEnabled ? Visibility.Collapsed : Visibility.Visible;
        var state = _webcamEnabled ? "On" : "Off";
        ToolTipService.SetToolTip(WebcamToggle, $"Webcam: {state}");
        AutomationProperties.SetName(WebcamToggle, $"Webcam {state}");
    }

    private void UpdateMouseClicksVisual()
    {
        var state = _showMouseClicks ? "On" : "Off";
        ToolTipService.SetToolTip(MouseClicksToggle, $"Mouse click visuals: {state}");
        AutomationProperties.SetName(MouseClicksToggle, $"Mouse click visuals {state}");
    }

    private void UpdateMicrophonePickerEnabled()
    {
        MicrophoneDeviceButton.IsEnabled = _recordMicrophone && !_microphonesLoading && MicrophoneFlyout.Items.Count > 0;
    }

    private void UpdateWebcamSettingsEnabled()
    {
        WebcamSettingsButton.IsEnabled = _captureType == CaptureType.Video;
    }

    private void RebuildMicrophoneFlyout(bool loading)
    {
        MicrophoneFlyout.Items.Clear();
        if (loading)
        {
            MicrophoneDeviceLabel.Text = "Loading...";
            MicrophoneFlyout.Items.Add(new MenuFlyoutItem { Text = "Loading microphones...", IsEnabled = false });
            return;
        }

        var selected = _microphones.FirstOrDefault(m => m.Id == _selectedMicrophoneId) ?? _microphones[0];
        MicrophoneDeviceLabel.Text = selected.Name;
        ToolTipService.SetToolTip(MicrophoneDeviceButton, $"Microphone device: {selected.Name}");

        foreach (var microphone in _microphones)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = microphone.Name,
                IsChecked = microphone.Id == _selectedMicrophoneId,
            };
            item.Click += (_, _) => SelectMicrophone(microphone);
            MicrophoneFlyout.Items.Add(item);
        }
    }

    private void RebuildWebcamSettingsFlyout(bool loading)
    {
        WebcamSettingsFlyout.Items.Clear();

        var cameraMenu = new MenuFlyoutSubItem { Text = "Camera" };
        if (loading)
        {
            cameraMenu.Items.Add(new MenuFlyoutItem { Text = "Loading webcams...", IsEnabled = false });
            WebcamSettingsFlyout.Items.Add(cameraMenu);
            AddWebcamLayoutItems();
            UpdateWebcamSettingsSummary();
            return;
        }

        var selected = _webcams.FirstOrDefault(w => w.Id == _selectedWebcamId) ?? _webcams[0];
        _selectedWebcamId = selected.Id;

        foreach (var webcam in _webcams)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = webcam.Name,
                IsChecked = webcam.Id == _selectedWebcamId,
            };
            item.Click += (_, _) => SelectWebcam(webcam);
            cameraMenu.Items.Add(item);
        }

        WebcamSettingsFlyout.Items.Add(cameraMenu);
        AddWebcamLayoutItems();
        UpdateWebcamSettingsSummary();
    }

    private void AddWebcamLayoutItems()
    {
        WebcamSettingsFlyout.Items.Add(new MenuFlyoutSeparator());

        var shapeMenu = new MenuFlyoutSubItem { Text = "Shape" };
        AddShapeItem(shapeMenu, "Rectangle", WebcamShape.Rectangle);
        AddShapeItem(shapeMenu, "Rounded rectangle", WebcamShape.RoundedRectangle);
        AddShapeItem(shapeMenu, "Circle", WebcamShape.Circle);
        WebcamSettingsFlyout.Items.Add(shapeMenu);

        var cornerMenu = new MenuFlyoutSubItem { Text = "Corner" };
        AddCornerItem(cornerMenu, "Top left", WebcamCornerPosition.TopLeft);
        AddCornerItem(cornerMenu, "Top right", WebcamCornerPosition.TopRight);
        AddCornerItem(cornerMenu, "Bottom left", WebcamCornerPosition.BottomLeft);
        AddCornerItem(cornerMenu, "Bottom right", WebcamCornerPosition.BottomRight);
        WebcamSettingsFlyout.Items.Add(cornerMenu);

        var sizeMenu = new MenuFlyoutSubItem { Text = "Size" };
        AddSizeItem(sizeMenu, "Small", WebcamSizePreset.Small);
        AddSizeItem(sizeMenu, "Medium", WebcamSizePreset.Medium);
        AddSizeItem(sizeMenu, "Large", WebcamSizePreset.Large);
        WebcamSettingsFlyout.Items.Add(sizeMenu);

        var radiusMenu = new MenuFlyoutSubItem
        {
            Text = "Rounded corner value",
            IsEnabled = _webcamShape == WebcamShape.RoundedRectangle,
        };
        foreach (var radius in WebcamCornerRadiusOptions)
        {
            AddCornerRadiusItem(radiusMenu, FormatCornerRadius(radius), radius);
        }

        WebcamSettingsFlyout.Items.Add(radiusMenu);
    }

    private void AddShapeItem(MenuFlyoutSubItem menu, string text, WebcamShape shape)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = text,
            IsChecked = _webcamShape == shape,
        };
        item.Click += (_, _) => SelectWebcamShape(shape);
        menu.Items.Add(item);
    }

    private void AddCornerItem(MenuFlyoutSubItem menu, string text, WebcamCornerPosition corner)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = text,
            IsChecked = _webcamCornerPosition == corner,
        };
        item.Click += (_, _) => SelectWebcamCorner(corner);
        menu.Items.Add(item);
    }

    private void AddSizeItem(MenuFlyoutSubItem menu, string text, WebcamSizePreset size)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = text,
            IsChecked = _webcamSizePreset == size,
        };
        item.Click += (_, _) => SelectWebcamSize(size);
        menu.Items.Add(item);
    }

    private void AddCornerRadiusItem(MenuFlyoutSubItem menu, string text, double radius)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = text,
            IsChecked = Math.Abs(_webcamCornerRadius - radius) < 0.1,
        };
        item.Click += (_, _) => SelectWebcamCornerRadius(radius);
        menu.Items.Add(item);
    }

    private void UpdateWebcamSettingsSummary()
    {
        var selected = _webcams.FirstOrDefault(w => w.Id == _selectedWebcamId);
        var deviceName = selected?.Name ?? (_webcamsLoading ? "Loading webcams..." : "System default");
        var state = _webcamEnabled ? "On" : "Off";
        var summary = $"Webcam settings: {state}, {deviceName}, {_webcamShape}, {_webcamSizePreset}, {FormatCorner(_webcamCornerPosition)}";
        ToolTipService.SetToolTip(WebcamSettingsButton, summary);
        AutomationProperties.SetName(WebcamSettingsButton, summary);
    }

    private static string FormatCorner(WebcamCornerPosition corner) => corner switch
    {
        WebcamCornerPosition.TopLeft => "top left",
        WebcamCornerPosition.TopRight => "top right",
        WebcamCornerPosition.BottomLeft => "bottom left",
        WebcamCornerPosition.BottomRight => "bottom right",
        _ => "bottom right",
    };

    private static string FormatCornerRadius(double radius) => radius < 0 ? "Default" : $"{radius:0} px";

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        GetCursorPos(out _dragCursorStart);
        _dragWindowStart = AppWindow.Position;
        _dragging = element.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        GetCursorPos(out var current);
        var dx = current.X - _dragCursorStart.X;
        var dy = current.Y - _dragCursorStart.Y;
        if (dx == 0 && dy == 0)
        {
            return;
        }

        AppWindow.Move(new PointInt32(_dragWindowStart.X + dx, _dragWindowStart.Y + dy));
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        _dragging = false;
        element.ReleasePointerCapture(e.Pointer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);
}
