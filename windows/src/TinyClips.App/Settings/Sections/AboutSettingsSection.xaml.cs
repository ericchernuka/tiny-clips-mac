using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TinyClips.Core.Services;

namespace TinyClips.App.Settings.Sections;

/// <summary>App version, build-flavor update messaging, and GitHub links.</summary>
public sealed partial class AboutSettingsSection : UserControl
{
    private const string WingetUpgradeCommand = "winget upgrade Refractored.TinyClips";

    private readonly IDisposable _realizationScope;
    private readonly IAppUpdateService _updateService;
    private Uri? _latestReleaseUri;

    public SettingsViewModel ViewModel { get; }

    public bool IsStoreBuild => BuildFlavor.IsStoreBuild;
    public bool IsDirectBuild => BuildFlavor.IsDirectBuild;

    public AboutSettingsSection(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        _realizationScope = viewModel.BeginSectionRealization();
        _updateService = App.Services.GetRequiredService<IAppUpdateService>();
        InitializeComponent();
        SectionLifecycle.HookFirstLoad(this, viewModel, _realizationScope);

        ApplyBuildFlavorVisibility();
        UpdateAboutInfo();
        ApplyUpdateCheckResult(_updateService.LastResult);
    }

    private void ApplyBuildFlavorVisibility()
    {
        DirectBuildUpdatesCard.Visibility = IsDirectBuild ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        StoreBuildUpdatesCard.Visibility = IsStoreBuild ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void UpdateAboutInfo()
    {
        var version = AppVersionInfo.GetCurrentVersionText();

        AboutVersionText.Text = $"Version {version}";
        AboutIssueLink.NavigateUri = BuildIssueRequestUri(version);
        AboutCopyrightText.Text = $"© {DateTime.Now.Year} Refractored LLC";
    }

    private void ApplyUpdateCheckResult(AppUpdateCheckResult? result)
    {
        if (!IsDirectBuild)
        {
            return;
        }

        if (result is null)
        {
            UpdateStatusText.Text = "Check for updates to see whether a newer version is available.";
            CopyWingetCommandButton.Visibility = Visibility.Collapsed;
            OpenLatestReleaseButton.Visibility = Visibility.Collapsed;
            _latestReleaseUri = null;
            return;
        }

        _latestReleaseUri = result.ReleaseUri;
        switch (result.Status)
        {
            case AppUpdateStatus.UpToDate:
                UpdateStatusText.Text = $"You're up to date (v{result.CurrentVersion}).";
                CopyWingetCommandButton.Visibility = Visibility.Collapsed;
                OpenLatestReleaseButton.Visibility = Visibility.Collapsed;
                break;
            case AppUpdateStatus.UpdateAvailable:
                UpdateStatusText.Text = $"Update available: v{result.LatestVersion} (current v{result.CurrentVersion}).";
                CopyWingetCommandButton.Visibility = Visibility.Visible;
                OpenLatestReleaseButton.Visibility = result.ReleaseUri is not null ? Visibility.Visible : Visibility.Collapsed;
                break;
            default:
                UpdateStatusText.Text = $"Couldn't check for updates: {result.Message ?? "Unknown error."}";
                CopyWingetCommandButton.Visibility = Visibility.Collapsed;
                OpenLatestReleaseButton.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private async void OnCheckForUpdatesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            CheckForUpdatesButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking for updates...";

            var result = await _updateService.CheckForUpdatesAsync(AppVersionInfo.GetCurrentVersion());
            ApplyUpdateCheckResult(result);
        }
        finally
        {
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    private async void OnCopyWingetCommandClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await ClipboardService.CopyTextAsync(WingetUpgradeCommand);
            UpdateStatusText.Text = "Copied: winget upgrade command. Run it in Terminal to update.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Copy winget command failed: {ex}");
            UpdateStatusText.Text = "Couldn't copy the winget command.";
        }
    }

    private void OnOpenLatestReleaseClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var target = _latestReleaseUri ?? new Uri("https://github.com/jamesmontemagno/tiny-clips/releases/latest");
            Process.Start(new ProcessStartInfo(target.ToString())
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Open latest release failed: {ex}");
            UpdateStatusText.Text = "Couldn't open the latest release page.";
        }
    }

    private static Uri BuildIssueRequestUri(string version)
    {
        const string repositoryIssuesNewUrl = "https://github.com/jamesmontemagno/tiny-clips/issues/new";
        var runtime = RuntimeInformation.OSDescription;
        var body =
            "### Details" + "\n" +
            $"- App: Tiny Clips for Windows" + "\n" +
            $"- Version: {version}" + "\n" +
            $"- OS: {runtime}" + "\n\n" +
            "### Describe your issue or feature request" + "\n" +
            "<!-- Tell us what happened or what you'd like to see -->";

        var title = "[Issue/Feature]: ";
        var query = $"title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
        return new Uri($"{repositoryIssuesNewUrl}?{query}");
    }
}
