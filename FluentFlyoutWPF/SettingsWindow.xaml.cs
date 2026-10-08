// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Pages;
using System.Text.RegularExpressions;
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
    private List<SearchItem> _allSearchItems = [];
    private string? _pendingHighlightElementId = null;
    static readonly Regex SplitCamelCaseRegex = new(@"(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);

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

        WindowStartupLocation = WindowStartupLocation.Manual;

        var settings = SettingsManager.Current;
        double targetWidth = settings.SettingsWindowWidth >= MinWidth ? settings.SettingsWindowWidth : 900;
        double targetHeight = settings.SettingsWindowHeight >= MinHeight ? settings.SettingsWindowHeight : 700;

        Rect workArea = GetWorkAreaForBounds(settings.SettingsWindowLeft, settings.SettingsWindowTop, targetWidth, targetHeight);

        targetWidth = Math.Max(MinWidth, Math.Min(targetWidth, workArea.Width));
        targetHeight = Math.Max(MinHeight, Math.Min(targetHeight, workArea.Height));

        double targetLeft = settings.SettingsWindowLeft;
        double targetTop = settings.SettingsWindowTop;

        if (double.IsNaN(targetLeft) || double.IsNaN(targetTop))
        {
            targetLeft = workArea.Left + (workArea.Width - targetWidth) / 2;
            targetTop = workArea.Top + (workArea.Height - targetHeight) / 2;
        }
        else
        {
            if (targetLeft + targetWidth > workArea.Right)
                targetLeft = workArea.Right - targetWidth;
            if (targetLeft < workArea.Left)
                targetLeft = workArea.Left;

            if (targetTop + targetHeight > workArea.Bottom)
                targetTop = workArea.Bottom - targetHeight;
            if (targetTop < workArea.Top)
                targetTop = workArea.Top;
        }

        Width = targetWidth;
        Height = targetHeight;
        Left = targetLeft;
        Top = targetTop;

        if (settings.SettingsWindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        LocationChanged += SettingsWindow_LocationChanged;
        SizeChanged += SettingsWindow_SizeChanged;
        StateChanged += SettingsWindow_StateChanged;

        Closed += (s, e) => instance = null;
        DataContext = SettingsManager.Current;

        RootNavigation.SetCurrentValue(NavigationView.IsPaneOpenProperty, false);
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

        if (navigationPage != null)
        {
            var pageType = System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetType($"FluentFlyoutWPF.Pages.{navigationPage}");
            if (pageType != null)
                NavigateToPage(pageType);
        }
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SearchItem selectedItem)
        {
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

        // Add specific settings deep links from auto-generated static array
        foreach (var item in SearchItems)
        {
            string title = Application.Current.TryFindResource(item.ResourceKey)?.ToString() ?? item.ResourceKey;
            // Clean up the page type name (e.g. "SystemPage" -> "System") and split camel case (e.g. "MediaFlyout" -> "Media Flyout")
            string pageName = SplitCamelCaseRegex.Replace(item.TargetPageType.Name.Replace("Page", ""), " ");
            items.Add(new SearchItem { Title = $"{title}", Subtitle = pageName, TargetPageType = item.TargetPageType, TargetElementId = item.TargetElementId });
        }

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
                        Duration = new Duration(TimeSpan.FromMilliseconds(300)),
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
                Task.Delay(300).ContinueWith(_ => ScrollToAndHighlight(elementId));
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

                    await Task.Delay(300);
                    RootNavigation.Navigate(typeof(HomePage));

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

    private void SettingsWindow_LocationChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            SettingsManager.Current.SettingsWindowLeft = Left;
            SettingsManager.Current.SettingsWindowTop = Top;
        }
    }

    private void SettingsWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            SettingsManager.Current.SettingsWindowWidth = e.NewSize.Width;
            SettingsManager.Current.SettingsWindowHeight = e.NewSize.Height;
            SettingsManager.Current.SettingsWindowLeft = Left;
            SettingsManager.Current.SettingsWindowTop = Top;
        }
    }

    private void SettingsWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SettingsManager.Current.SettingsWindowState = WindowState.Maximized;
        }
        else if (WindowState == WindowState.Normal)
        {
            SettingsManager.Current.SettingsWindowState = WindowState.Normal;
        }
    }

    private void SettingsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            SettingsManager.Current.SettingsWindowWidth = ActualWidth;
            SettingsManager.Current.SettingsWindowHeight = ActualHeight;
            SettingsManager.Current.SettingsWindowLeft = Left;
            SettingsManager.Current.SettingsWindowTop = Top;
            SettingsManager.Current.SettingsWindowState = WindowState.Normal;
        }
        else if (RestoreBounds.Width >= MinWidth && RestoreBounds.Height >= MinHeight)
        {
            SettingsManager.Current.SettingsWindowWidth = RestoreBounds.Width;
            SettingsManager.Current.SettingsWindowHeight = RestoreBounds.Height;
            SettingsManager.Current.SettingsWindowLeft = RestoreBounds.Left;
            SettingsManager.Current.SettingsWindowTop = RestoreBounds.Top;
            if (WindowState == WindowState.Maximized)
            {
                SettingsManager.Current.SettingsWindowState = WindowState.Maximized;
            }
        }

        SettingsManager.SaveSettings();
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

    private static Rect GetWorkAreaForBounds(double left, double top, double width, double height)
    {
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            return SystemParameters.WorkArea;
        }

        try
        {
            var monitors = MonitorUtil.GetMonitors();
            var targetRect = new Rect(left, top, Math.Max(1, width), Math.Max(1, height));
            Rect bestWorkArea = Rect.Empty;
            double maxIntersectionArea = -1;

            foreach (var monitor in monitors)
            {
                double dpiX = monitor.dpiX > 0 ? monitor.dpiX : 96.0;
                double dpiY = monitor.dpiY > 0 ? monitor.dpiY : 96.0;
                var monitorWorkAreaDips = new Rect(
                    monitor.workArea.Left * 96.0 / dpiX,
                    monitor.workArea.Top * 96.0 / dpiY,
                    monitor.workArea.Width * 96.0 / dpiX,
                    monitor.workArea.Height * 96.0 / dpiY);

                var intersection = Rect.Intersect(targetRect, monitorWorkAreaDips);
                double area = (!intersection.IsEmpty) ? intersection.Width * intersection.Height : 0;

                if (area > maxIntersectionArea)
                {
                    maxIntersectionArea = area;
                    bestWorkArea = monitorWorkAreaDips;
                }
            }

            if (!bestWorkArea.IsEmpty && maxIntersectionArea > 0)
            {
                return bestWorkArea;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error getting work area for window bounds");
        }

        return SystemParameters.WorkArea;
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
        public string? Subtitle { get; set; }
        public Type? TargetPageType { get; set; }
        public string? TargetElementId { get; set; }
        public override string ToString() => Title;
    }
}