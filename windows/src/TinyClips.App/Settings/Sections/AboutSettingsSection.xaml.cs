using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace TinyClips.App.Settings.Sections;

/// <summary>App version, build-flavor update messaging, and GitHub links.</summary>
public sealed partial class AboutSettingsSection : UserControl
{
    private readonly IDisposable _realizationScope;
    private string _appVersion = "1.0.0";

    public SettingsViewModel ViewModel { get; }

    public bool IsStoreBuild => BuildFlavor.IsStoreBuild;
    public bool IsDirectBuild => BuildFlavor.IsDirectBuild;

    public AboutSettingsSection(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        _realizationScope = viewModel.BeginSectionRealization();
        InitializeComponent();
        SectionLifecycle.HookFirstLoad(this, viewModel, _realizationScope);

        ApplyBuildFlavorVisibility();
        UpdateAboutInfo();
    }

    private void ApplyBuildFlavorVisibility()
    {
        DirectBuildUpdatesCard.Visibility = IsDirectBuild ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        StoreBuildUpdatesCard.Visibility = IsStoreBuild ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void UpdateAboutInfo()
    {
        _appVersion = QuickBugReport.GetAppVersion();
        AboutVersionText.Text = $"Version {_appVersion}";
        AboutDetailedIssueLink.NavigateUri = QuickBugReport.BuildDetailedIssueRequestUri(_appVersion);
        AboutCopyrightText.Text = $"© {DateTime.Now.Year} Refractored LLC";
    }

    private async void OnFileBugClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await OpenQuickBugReportAsync();
    }

    private Task OpenQuickBugReportAsync()
        => QuickBugReport.ShowQuickBugDialogAndOpenAsync(
            XamlRoot,
            _appVersion,
            QuickBugReport.GetDistributionChannel()
        );
}
