// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Pages;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF;

public partial class SettingsWindow : FluentWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static SettingsWindow? instance;
    private Type? _currentPageType;
    private ScrollViewer? _contentScrollViewer;
    private List<SearchItem> _allSearchItems = new();
    private string? _pendingHighlightElementId = null;

    public SettingsWindow()
    {
        if (instance != null)
        {
            if (instance.WindowState == WindowState.Minimized)
            {
                instance.WindowState = WindowState.Normal;
            }

            instance.Activate();
            instance.Focus();
            Close();
            return;
        }

        InitializeComponent();
        instance = this;
        this.SizeChanged += SettingsWindow_SizeChanged;

        Closed += (s, e) => instance = null;
        DataContext = SettingsManager.Current;

        RootNavigation.SetCurrentValue(NavigationView.IsPaneOpenProperty, false);
    }

    private void SettingsWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSearchBoxPosition();
    }

    private void UpdateSearchBoxPosition()
    {
        if (SearchGrid.IsLoaded)
        {
            try
            {
                var transform = SearchGrid.TransformToAncestor(this);
                Point gridPos = transform.Transform(new Point(0, 0));

                double leftBound = gridPos.X;
                double rightBound = this.ActualWidth - 150; // 150px approx width of window control buttons

                if (rightBound > leftBound)
                {
                    double availableWidth = rightBound - leftBound;
                    SearchGrid.Width = Math.Max(availableWidth, SearchBox.Width);
                }
                else
                {
                    SearchGrid.Width = SearchBox.Width;
                }
            }
            catch { }
        }
    }

    public static void ShowInstance(string? navigationPage = null)
    {
        if (instance == null)
        {
            new SettingsWindow().Show();
            instance?.Activate();
        }
        else
        {
            if (instance.WindowState == WindowState.Minimized)
            {
                instance.WindowState = WindowState.Normal;
            }

            instance.Activate();
            instance.Focus();
        }
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SearchItem selectedItem)
        {
            // Clear the search text asynchronously to avoid being overwritten by the AutoSuggestBox setting the text to the selected item
            Dispatcher.BeginInvoke(new Action(() =>
            {
                sender.Text = string.Empty;
                sender.IsSuggestionListOpen = false;
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            if (selectedItem.TargetPageType != null)
            {
                if (_currentPageType != selectedItem.TargetPageType)
                {
                    _pendingHighlightElementId = selectedItem.TargetElementId;
                    RootNavigation.Navigate(selectedItem.TargetPageType);
                }
                else if (!string.IsNullOrEmpty(selectedItem.TargetElementId))
                {
                    // Already on the page, just scroll and highlight
                    ScrollToAndHighlight(selectedItem.TargetElementId);
                }
            }
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var query = sender.Text.ToLowerInvariant();
            var matches = _allSearchItems.Where(x => x.Title.ToLowerInvariant().Contains(query)).ToList();
            sender.ItemsSource = matches;
            sender.IsSuggestionListOpen = matches.Count > 0;
        }
    }

    private void FluentWindow_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SearchBox.IsKeyboardFocusWithin && !SearchBox.IsMouseOver)
        {
            SearchBox.IsSuggestionListOpen = false;
            // Move focus to the window to unfocus the search box
            this.Focus();
        }
    }

    public static void NavigateToPage(Type pageType)
    {
        instance?.RootNavigation.Navigate(pageType);
    }

    private void BuildSearchItems()
    {
        var items = new List<SearchItem>();

        // Add all tabs
        foreach (var navItem in RootNavigation.MenuItems.OfType<NavigationViewItem>().Concat(RootNavigation.FooterMenuItems.OfType<NavigationViewItem>()))
        {
            if (navItem.Content != null)
            {
                items.Add(new SearchItem { Title = navItem.Content.ToString()!, TargetPageType = navItem.TargetPageType });
            }
        }

        // Add specific settings deep links
        void AddDeepLink(Type targetPageType, string resourceKey, string elementId)
        {
            string title = Application.Current.TryFindResource(resourceKey)?.ToString() ?? resourceKey;
            // Clean up the page type name (e.g. "SystemPage" -> "System")
            string pageName = targetPageType.Name.Replace("Page", "");
            items.Add(new SearchItem { Title = $"{title} ({pageName})", TargetPageType = targetPageType, TargetElementId = elementId });
        }

        AddDeepLink(typeof(Pages.AdvancedPage), "MediaFlyoutTitle", "MediaFlyoutTitleCard");
        AddDeepLink(typeof(Pages.AdvancedPage), "NextUpCustomizationTitle", "NextUpCustomizationTitleCard");
        AddDeepLink(typeof(Pages.AdvancedPage), "LockKeysCustomizationTitle", "LockKeysCustomizationTitleCard");
        AddDeepLink(typeof(Pages.AdvancedPage), "LegacyTaskbarWidthTitle", "LegacyTaskbarWidthTitleCard");
        AddDeepLink(typeof(Pages.AppFilteringPage), "EnableAppFilteringTitle", "EnableAppFilteringTitleCard");
        AddDeepLink(typeof(Pages.AppFilteringPage), "AppFilteringModeTitle", "AppFilteringModeTitleCard");
        AddDeepLink(typeof(Pages.HomePage), "UnlockFullExperienceText", "UnlockFullExperienceTextCard");
        AddDeepLink(typeof(Pages.LockKeysPage), "EnableLockKeysTitle", "EnableLockKeysTitleCard");
        AddDeepLink(typeof(Pages.LockKeysPage), "LockKeysStayDurationTitle", "LockKeysStayDurationTitleCard");
        AddDeepLink(typeof(Pages.LockKeysPage), "LockKeysBoldUITitle", "LockKeysBoldUITitleCard");
        AddDeepLink(typeof(Pages.LockKeysPage), "LockKeysAnimatedTitle", "LockKeysAnimatedTitleCard");
        AddDeepLink(typeof(Pages.LockKeysPage), "LockKeysCursorUITitle", "LockKeysCursorUITitleCard");
        AddDeepLink(typeof(Pages.LockKeysPage), "EnableInsertKeyTitle", "EnableInsertKeyTitleCard");
        AddDeepLink(typeof(Pages.LockKeysPage), "EnableCapsTitle", "EnableCapsTitleCard");
        AddDeepLink(typeof(Pages.LockKeysPage), "EnableNumTitle", "EnableNumTitleCard");
        AddDeepLink(typeof(Pages.LockKeysPage), "EnableScrollTitle", "EnableScrollTitleCard");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "TextBlockText1", "TextBlockText1Card");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "BackgroundBlurTitle", "BackgroundBlurTitleCard");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "CompactLayoutTitle", "CompactLayoutTitleCard");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "FlyoutPositionTitle", "FlyoutPositionTitleCard");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "FlyoutStayDurationTitle", "FlyoutStayDurationTitleCard");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "MediaFlyoutAlwaysDisplay", "MediaFlyoutAlwaysDisplayCard");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "CenterTitleArtistTitle", "CenterTitleArtistTitleCard");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "ShowMediaPlayerNameTitle", "ShowMediaPlayerNameTitleCard");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "RepeatButtonTitle", "RepeatButtonTitleCard");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "ShuffleButtonTitle", "ShuffleButtonTitleCard");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "ShowSeekbarTitle", "ShowSeekbarTitleCard");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "PauseOtherMediaTitle", "PauseOtherMediaTitleCard");
        AddDeepLink(typeof(Pages.MediaFlyoutPage), "MediaFlyoutExcludeVolumeTitle", "MediaFlyoutExcludeVolumeTitleCard");
        AddDeepLink(typeof(Pages.NextUpPage), "EnableNextUpTitle", "EnableNextUpTitleCard");
        AddDeepLink(typeof(Pages.NextUpPage), "NextUpStayDurationTitle", "NextUpStayDurationTitleCard");
        AddDeepLink(typeof(Pages.SystemPage), "SelectedMonitorTitle", "SelectedMonitorCard");
        AddDeepLink(typeof(Pages.SystemPage), "AcrylicBlurOpacityTitle", "AcrylicBlurOpacityCard");
        AddDeepLink(typeof(Pages.SystemPage), "UseAlbumArtAccentColorTitle", "UseAlbumArtAccentColorCard");
        AddDeepLink(typeof(Pages.SystemPage), "AppLanguageTitle", "AppLanguageCard");
        AddDeepLink(typeof(Pages.SystemPage), "AppThemeTitle", "AppThemeCard");
        AddDeepLink(typeof(Pages.SystemPage), "LaunchOnStartupTitle", "LaunchOnStartupCard");
        AddDeepLink(typeof(Pages.SystemPage), "AnonymousUsageDataTitle", "AnonymousUsageDataCard");
        AddDeepLink(typeof(Pages.SystemPage), "DisableOnFullscreenTitle", "DisableOnFullscreenCard");
        AddDeepLink(typeof(Pages.SystemPage), "TrayIconLeftClickBehaviorTitle", "TrayIconLeftClickBehaviorCard");
        AddDeepLink(typeof(Pages.SystemPage), "Win11TrayIconTitle", "Win11TrayIconCard");
        AddDeepLink(typeof(Pages.SystemPage), "HideTrayIconTitle", "HideTrayIconCard");
        AddDeepLink(typeof(Pages.SystemPage), "BackupRestoreCardTitle", "BackupRestoreCardTitleCard");
        AddDeepLink(typeof(Pages.SystemPage), "ShowUpdateNotificationsTitle", "ShowUpdateNotificationsTitleCard");
        AddDeepLink(typeof(Pages.SystemPage), "AppFilteringTitle", "AppFilteringTitleCard");
        AddDeepLink(typeof(Pages.SystemPage), "AdvancedSettingsTitle", "AdvancedSettingsTitleCard");
        AddDeepLink(typeof(Pages.TaskbarVisualizerPage), "TaskbarVisualizerEnabledTitle", "TaskbarVisualizerEnabledTitleCard");
        AddDeepLink(typeof(Pages.TaskbarVisualizerPage), "TaskbarVisualizerPositionTitle", "TaskbarVisualizerPositionTitleCard");
        AddDeepLink(typeof(Pages.TaskbarVisualizerPage), "TaskbarVisualizerBarCountTitle", "TaskbarVisualizerBarCountTitleCard");
        AddDeepLink(typeof(Pages.TaskbarVisualizerPage), "TaskbarVisualizerCenteredBarsTitle", "TaskbarVisualizerCenteredBarsTitleCard");
        AddDeepLink(typeof(Pages.TaskbarVisualizerPage), "TaskbarVisualizerBaselineTitle", "TaskbarVisualizerBaselineTitleCard");
        AddDeepLink(typeof(Pages.TaskbarVisualizerPage), "TaskbarVisualizerAudioSensitivityTitle", "TaskbarVisualizerAudioSensitivityTitleCard");
        AddDeepLink(typeof(Pages.TaskbarVisualizerPage), "TaskbarVisualizerAudioPeakLevelTitle", "TaskbarVisualizerAudioPeakLevelTitleCard");
        AddDeepLink(typeof(Pages.TaskbarVisualizerPage), "AccentTextFillColorPrimaryBrush", "StartupHyperlink");
        AddDeepLink(typeof(Pages.TaskbarVisualizerPage), "TaskbarVisualizerClickableTitle", "TaskbarVisualizerClickableTitleCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetEnabledTitle", "TaskbarWidgetEnabledTitleCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetPosition", "TaskbarWidgetPositionCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetSelectedMonitorTitle", "TaskbarWidgetSelectedMonitorTitleCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetPaddingTitle", "TaskbarWidgetPaddingTitleCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetManualPaddingTitle", "TaskbarWidgetManualPaddingTitleCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetBackgroundBlurTitle", "TaskbarWidgetBackgroundBlurTitleCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetHideCompletelyTitle", "TaskbarWidgetHideCompletelyTitleCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetFixedWidthTitle", "TaskbarWidgetFixedWidthTitleCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetAutoHideTitle", "TaskbarWidgetAutoHideTitleCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetControlsTitle", "TaskbarWidgetControlsTitleCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetControlsPositionTitle", "TaskbarWidgetControlsPositionTitleCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetAnimatedTitle", "TaskbarWidgetAnimatedTitleCard");
        AddDeepLink(typeof(Pages.TaskbarWidgetPage), "TaskbarWidgetShowPauseOverlayTitle", "TaskbarWidgetShowPauseOverlayTitleCard");
        AddDeepLink(typeof(Pages.VolumeMixerPage), "EnableVolumeFlyoutTitle", "EnableVolumeFlyoutTitleCard");
        AddDeepLink(typeof(Pages.VolumeMixerPage), "VolumeAboveMediaFlyoutTitle", "VolumeAboveMediaFlyoutTitleCard");
        AddDeepLink(typeof(Pages.VolumeMixerPage), "VolumeFlyoutStayDurationTitle", "VolumeFlyoutStayDurationTitleCard");
        AddDeepLink(typeof(Pages.VolumeMixerPage), "EnableVolumeMixerTitle", "EnableVolumeMixerTitleCard");
        AddDeepLink(typeof(Pages.VolumeMixerPage), "VolumeMixerHighlightTitle", "VolumeMixerHighlightTitleCard");

        _allSearchItems = items;
        SearchBox.OriginalItemsSource = _allSearchItems;
    }

    private void ScrollToAndHighlight(string elementId)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var targetElement = FindChildByName<FrameworkElement>(RootNavigation, elementId);
                if (targetElement != null)
                {
                    targetElement.BringIntoView();

                    // Heartbeat animation
                    var heartbeatAnimation = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 1.0,
                        To = 0.5,
                        Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                        AutoReverse = true,
                        RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2)
                    };
                    targetElement.BeginAnimation(UIElement.OpacityProperty, heartbeatAnimation);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error scrolling to and highlighting element");
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateSearchBoxPosition();
        RootNavigation.IsPaneOpen = false;

        _currentPageType = typeof(HomePage);
        RootNavigation.Navigate(_currentPageType);

        // wrkaround for WPF-UI NavigationView theme change bug:
        // force pane initialization by toggling it once to prevent width corruption on theme changes
        // not sure why this has to be done
        await Task.Delay(100);
        RootNavigation.IsPaneOpen = true;
        await Task.Delay(10);
        RootNavigation.IsPaneOpen = false;

        LicenseManager.GetPremiumProductInfo();

        RootNavigation.Navigated += (s, args) =>
        {
            _currentPageType = args.Page?.GetType();
            ResetScrollPosition();
            if (!string.IsNullOrEmpty(_pendingHighlightElementId))
            {
                var elementId = _pendingHighlightElementId;
                _pendingHighlightElementId = null;
                // Add a slight delay to ensure page is fully rendered before finding child and scrolling
                Task.Delay(150).ContinueWith(_ => ScrollToAndHighlight(elementId));
            }
        };

        SettingsManager.Current.PropertyChanged += async (s, args) =>
        {
            if (args.PropertyName == nameof(SettingsManager.Current.AppTheme))
            {
                var wasPaneOpen = RootNavigation.IsPaneOpen;

                // force fix pane state after theme change
                await Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(100);
                    RootNavigation.IsPaneOpen = !wasPaneOpen;
                    await Task.Delay(10);
                    RootNavigation.IsPaneOpen = wasPaneOpen;

                    BuildSearchItems();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else if (args.PropertyName == nameof(SettingsManager.Current.AppLanguage))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    BuildSearchItems();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };

        BuildSearchItems();
    }

    private void SettingsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SettingsManager.SaveSettings();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.SaveSettings();
        Close();
    }

    private void ResetScrollPosition()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                _contentScrollViewer ??= FindScrollableScrollViewer(RootNavigation);

                if (_contentScrollViewer != null)
                {
                    _contentScrollViewer.ScrollToVerticalOffset(0);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error resetting scroll position in SettingsWindow");
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // helper functions to traverse visual tree

    private static T? FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild && typedChild.Name == name)
            {
                return typedChild;
            }

            var result = FindChildByName<T>(child, name);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private static ScrollViewer? FindScrollableScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv && sv.ScrollableHeight > 0)
            {
                return sv;
            }

            var result = FindScrollableScrollViewer(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var result = FindVisualChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    public class SearchItem
    {
        public string Title { get; set; } = string.Empty;
        public Type? TargetPageType { get; set; }
        public string? TargetElementId { get; set; }
        public override string ToString() => Title;
    }
}