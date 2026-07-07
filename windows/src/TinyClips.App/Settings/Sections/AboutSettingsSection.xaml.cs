using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace TinyClips.App.Settings.Sections;

/// <summary>App version, build-flavor update messaging, and GitHub links.</summary>
public sealed partial class AboutSettingsSection : UserControl
{
    private readonly IDisposable _realizationScope;

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
        var version = "1.0.0";
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // Unpackaged runs can't query the package version; fall back to the assembly version.
            var asmVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (asmVersion is not null)
            {
                version = asmVersion.ToString();
            }
        }

        AboutVersionText.Text = $"Version {version}";
        AboutIssueLink.NavigateUri = BuildIssueRequestUri(version);
        AboutCopyrightText.Text = $"© {DateTime.Now.Year} Refractored LLC";
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
