using System;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TinyClips.Core.Models;
using Windows.System;
using Windows.UI.Core;

namespace TinyClips.App.Settings.Sections;

/// <summary>Screenshot/video/GIF global hotkey display, editing, and reset.</summary>
public sealed partial class HotkeysSettingsSection : UserControl
{
    private readonly IDisposable _realizationScope;

    public SettingsViewModel ViewModel { get; }

    public HotkeysSettingsSection(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        _realizationScope = viewModel.BeginSectionRealization();
        InitializeComponent();
        SectionLifecycle.HookFirstLoad(this, viewModel, _realizationScope);
    }

    private static CaptureType TypeFromTag(object? tag) => (tag as string) switch
    {
        "Video" => CaptureType.Video,
        "Gif" => CaptureType.Gif,
        _ => CaptureType.Screenshot,
    };

    private async void OnEditHotKey(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            await RecordShortcutAsync(TypeFromTag(element.Tag));
        }
    }

    private void OnResetHotKey(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        ViewModel.ResetHotKey(TypeFromTag(element.Tag));
        (App.Current as App)?.ReapplyGlobalHotKeys();
    }

    private async Task RecordShortcutAsync(CaptureType type)
    {
        var prompt = new TextBlock
        {
            Text = "Press the key combination you want (include Ctrl, Alt, Shift, or Win).",
            TextWrapping = TextWrapping.Wrap,
        };

        var dialog = new ContentDialog
        {
            Title = "Set shortcut",
            Content = prompt,
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };

        HotKeyModifiers chosenModifiers = 0;
        uint chosenKey = 0;

        void OnKey(object s, KeyRoutedEventArgs args)
        {
            args.Handled = true;

            if (IsModifierKey(args.Key))
            {
                return;
            }

            var modifiers = CurrentModifiers();
            if (modifiers == 0)
            {
                prompt.Text = "Please include at least one modifier (Ctrl, Alt, Shift, or Win).";
                return;
            }

            chosenModifiers = modifiers;
            chosenKey = (uint)args.Key;
            dialog.Hide();
        }

        dialog.KeyDown += OnKey;
        await dialog.ShowAsync();
        dialog.KeyDown -= OnKey;

        if (chosenKey != 0)
        {
            ViewModel.SetHotKey(type, chosenModifiers, chosenKey);
            (App.Current as App)?.ReapplyGlobalHotKeys();
        }
    }

    private static bool IsModifierKey(VirtualKey key) => key is
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or
        VirtualKey.LeftWindows or VirtualKey.RightWindows;

    private static HotKeyModifiers CurrentModifiers()
    {
        HotKeyModifiers modifiers = 0;
        if (IsKeyDown(VirtualKey.Control))
        {
            modifiers |= HotKeyModifiers.Control;
        }

        if (IsKeyDown(VirtualKey.Shift))
        {
            modifiers |= HotKeyModifiers.Shift;
        }

        if (IsKeyDown(VirtualKey.Menu))
        {
            modifiers |= HotKeyModifiers.Alt;
        }

        if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows))
        {
            modifiers |= HotKeyModifiers.Win;
        }

        return modifiers;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
