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
    bool ShowMouseClicks,
    int VideoTimeLimitMinutes);

/// <summary>
/// Pre-recording setup panel shown after target selection and before countdown.
/// </summary>
public sealed partial class RecordingSetupWindow : Window
{
    private static readonly int[] LimitOptions = { 0, 1, 3, 5, 10, 15, 30, 45, 60 };

    private const int TopOffsetDip = 24;
    private const int RegionOutsideOffsetDip = 12;
    private const uint WdaExcludeFromCapture = 0x11;

    private const string MicGlyph = "\uE720";
    private const string SystemAudioOnGlyph = "\uE767";
    private const string SystemAudioOffGlyph = "\uE74F";

    private readonly TaskCompletionSource<RecordingSetupResult?> _result = new();
    private readonly CaptureType _captureType;
    private readonly IAudioDeviceService _audioDevices;
    private readonly ObservableCollection<AudioInputDevice> _microphones = new();

    private bool _completed;
    private bool _closed;
    private bool _suppressEvents;
    private bool _recordSystemAudio;
    private bool _recordMicrophone;
    private string _selectedMicrophoneId;
    private bool _showMouseClicks;
    private int _videoTimeLimitMinutes;

    private bool _dragging;
    private POINT _dragCursorStart;
    private PointInt32 _dragWindowStart;

    private RecordingSetupWindow(CaptureType captureType, ICaptureSettings settings, IAudioDeviceService audioDevices)
    {
        InitializeComponent();

        _captureType = captureType;
        _audioDevices = audioDevices;
        _recordSystemAudio = settings.RecordAudio;
        _recordMicrophone = settings.RecordMicrophone;
        _selectedMicrophoneId = settings.SelectedMicrophoneId ?? string.Empty;
        _showMouseClicks = settings.ShouldShowMouseClickVisuals(captureType);
        _videoTimeLimitMinutes = Math.Max(0, settings.VideoRecordingTimeLimitMinutes);

        MicrophoneCombo.ItemsSource = _microphones;
        _microphones.Add(new AudioInputDevice(string.Empty, "System default"));
        MicrophoneCombo.SelectedItem = _microphones[0];

        ConfigurePresenter();
        ConfigureForCaptureType();
        BuildLimitFlyout();
        UpdateVisuals();

        Closed += OnClosed;
    }

    public static Task<RecordingSetupResult?> RunAsync(
        CaptureType captureType,
        ICaptureSettings settings,
        IAudioDeviceService audioDevices,
        MonitorInfo? monitor,
        PixelRect? regionInVirtualDesktop)
    {
        var window = new RecordingSetupWindow(captureType, settings, audioDevices);
        window.ShowNear(monitor, regionInVirtualDesktop);
        if (captureType == CaptureType.Video)
        {
            _ = window.LoadMicrophonesAsync();
        }

        return window._result.Task;
    }

    private void ConfigureForCaptureType()
    {
        if (_captureType == CaptureType.Gif)
        {
            SystemAudioToggle.Visibility = Visibility.Collapsed;
            MicrophoneToggle.Visibility = Visibility.Collapsed;
            MicrophonePickerHost.Visibility = Visibility.Collapsed;
            LimitButton.Visibility = Visibility.Collapsed;
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
            MicrophoneCombo.SelectedItem = selected;
        }
        finally
        {
            _suppressEvents = false;
            UpdateMicrophonePickerEnabled();
        }
    }

    private void SetMicrophoneLoading(bool loading)
    {
        if (_closed)
        {
            return;
        }

        MicrophoneLoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        MicrophoneCombo.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
        UpdateMicrophonePickerEnabled();
    }

    private void BuildLimitFlyout()
    {
        foreach (var minutes in LimitOptions)
        {
            var item = new MenuFlyoutItem { Text = minutes == 0 ? "No limit" : $"{minutes} min" };
            item.Click += (_, _) =>
            {
                _videoTimeLimitMinutes = minutes;
                UpdateLimitLabel();
            };
            LimitFlyout.Items.Add(item);
        }
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

        var width = (int)Math.Ceiling(RootGrid.DesiredSize.Width * scale);
        var height = (int)Math.Ceiling(RootGrid.DesiredSize.Height * scale);
        var topOffset = (int)Math.Round(TopOffsetDip * scale);
        var regionOutsideOffset = (int)Math.Round(RegionOutsideOffsetDip * scale);

        AppWindow.Resize(new SizeInt32(width + 2, height + 2));

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

    private void OnMicrophoneToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _recordMicrophone = MicrophoneToggle.IsChecked == true;
        UpdateMicrophoneVisual();
        UpdateMicrophonePickerEnabled();
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

    private void OnMicrophoneSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        if (MicrophoneCombo.SelectedItem is AudioInputDevice microphone)
        {
            _selectedMicrophoneId = microphone.Id;
        }
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        var selectedMicrophoneId = MicrophoneCombo.SelectedItem is AudioInputDevice microphone
            ? microphone.Id
            : _selectedMicrophoneId;
        Complete(new RecordingSetupResult(
            _captureType == CaptureType.Video && _recordSystemAudio,
            _captureType == CaptureType.Video && _recordMicrophone,
            selectedMicrophoneId,
            _showMouseClicks,
            _videoTimeLimitMinutes));
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
            MouseClicksToggle.IsChecked = _showMouseClicks;
        }
        finally
        {
            _suppressEvents = false;
        }

        UpdateSystemAudioVisual();
        UpdateMicrophoneVisual();
        UpdateMouseClicksVisual();
        UpdateMicrophonePickerEnabled();
        UpdateLimitLabel();
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

    private void UpdateMouseClicksVisual()
    {
        var state = _showMouseClicks ? "On" : "Off";
        ToolTipService.SetToolTip(MouseClicksToggle, $"Mouse click visuals: {state}");
        AutomationProperties.SetName(MouseClicksToggle, $"Mouse click visuals {state}");
    }

    private void UpdateMicrophonePickerEnabled()
    {
        MicrophoneCombo.IsEnabled = _recordMicrophone && MicrophoneLoadingPanel.Visibility != Visibility.Visible;
    }

    private void UpdateLimitLabel()
    {
        LimitLabel.Text = _videoTimeLimitMinutes <= 0 ? "No limit" : $"{_videoTimeLimitMinutes} min";
        AutomationProperties.SetName(
            LimitButton,
            $"Recording time limit, {(_videoTimeLimitMinutes <= 0 ? "no limit" : _videoTimeLimitMinutes + " minutes")}");
    }

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
