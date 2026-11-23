// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Services;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF.Windows;

public partial class ChangelogWindow : FluentWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly string _lastViewedVersion;
    private readonly string _currentVersion;
    private readonly CancellationTokenSource _loadCancellation = new();
    private readonly Stopwatch _openDuration = Stopwatch.StartNew();
    private string _viewAllUrl = string.Empty;
    private NavigationViewItem? _activeReleaseItem;
    private CancellationTokenSource? _renderCancellation;
    private bool _versionRecorded;

    public ChangelogWindow(string lastViewedVersion, string currentVersion)
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
        _lastViewedVersion = lastViewedVersion;
        _currentVersion = currentVersion;
    }

    private async void ChangelogWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var feed = await ChangelogService.GetReleasesAsync(
                _lastViewedVersion,
                _loadCancellation.Token);

            if (_loadCancellation.IsCancellationRequested)
            {
                return;
            }

            foreach (var release in feed.Releases)
            {
                var item = new NavigationViewItem
                {
                    Content = release,
                    ContentTemplate = (DataTemplate)FindResource("ReleaseNavigationContent"),
                };
                item.Click += ReleaseNavigationItem_Click;
                ReleaseNavigation.MenuItems.Add(item);
                item.Template = (ControlTemplate)FindResource("ReleaseNavigationItemTemplate");
            }
            _viewAllUrl = feed.ViewAllUrl;
            ViewAllButton.Visibility = Visibility.Visible;

            if (ReleaseNavigation.MenuItems.Count > 0
                && ReleaseNavigation.MenuItems[0] is NavigationViewItem firstItem)
            {
                await SelectReleaseAsync(firstItem);
            }
        }
        catch (OperationCanceledException) when (_loadCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load changelog releases");
            ShowLoadError();
        }
    }

    private async void ReleaseNavigationItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is NavigationViewItem item
            && (item != _activeReleaseItem || OpenOnlineButton.Visibility == Visibility.Visible))
        {
            await SelectReleaseAsync(item);
        }
    }

    private async Task SelectReleaseAsync(NavigationViewItem item)
    {
        if (item.Content is not ChangelogRelease release)
        {
            return;
        }

        _renderCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_loadCancellation.Token);
        _renderCancellation = cancellation;
        _activeReleaseItem?.Deactivate(ReleaseNavigation);
        _activeReleaseItem = item;
        item.Activate(ReleaseNavigation);
        ReleaseContentScroll.Visibility = Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
        LoadingRing.Visibility = Visibility.Visible;
        OpenOnlineButton.Visibility = Visibility.Collapsed;
        StatusText.Text = "";

        try
        {
            var content = new StackPanel();
            await ChangelogRenderer.RenderAsync(release.ContentHtml, content, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            ReleaseTitle.Text = release.NavigationTitle;
            ReleaseDate.Text = release.ReleaseDate;
            ReleaseContent.Content = content;
            ReleaseContentScroll.ScrollToTop();
            ReleaseContentScroll.Visibility = Visibility.Visible;
            await WaitForRenderAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            StatusPanel.Visibility = Visibility.Collapsed;

            if (!_versionRecorded && ChangelogService.HasUnseenUpdate(_currentVersion, _lastViewedVersion))
            {
                SettingsManager.Current.LastViewedChangelogVersion = _currentVersion;
                SettingsManager.SaveSettings();
                _versionRecorded = true;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to render changelog release {0}", release.Version);
            ReleaseContentScroll.Visibility = Visibility.Collapsed;
            ShowLoadError();
        }
        finally
        {
            if (_renderCancellation == cancellation)
            {
                _renderCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private async Task WaitForRenderAsync(CancellationToken cancellationToken)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) => rendered.TrySetResult();
        CompositionTarget.Rendering += handler;
        using var registration = cancellationToken.Register(() => rendered.TrySetCanceled(cancellationToken));
        try
        {
            await rendered.Task;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle, cancellationToken);
        }
        finally
        {
            CompositionTarget.Rendering -= handler;
        }
    }

    private void ShowLoadError()
    {
        LoadingRing.Visibility = Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = Application.Current.TryFindResource("ChangelogLoadFailedText")?.ToString();
        OpenOnlineButton.Visibility = Visibility.Visible;
    }

    private void OpenOnlineButton_Click(object sender, RoutedEventArgs e)
    {
        Notifications.OpenChangelogInBrowser();
    }

    private void ViewAllButton_Click(object sender, RoutedEventArgs e)
    {
        Notifications.OpenUrlInBrowser(_viewAllUrl);
    }

    private void ChangelogWindow_Closed(object? sender, EventArgs e)
    {
        _loadCancellation.Cancel();
        _loadCancellation.Dispose();
        _openDuration.Stop();

        _ = TelemetryService.SendTelemetryEventAsync(
            "changelog_window_duration",
            eventData: new Dictionary<string, object>
            {
                ["durationMs"] = _openDuration.ElapsedMilliseconds
            });
    }
}