// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;

using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class AppFilteringPage : Page
{
    public AppFilteringPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
    }

    /// <summary>
    /// Saves the current settings and refreshes the media sessions in the main window.
    /// This ensures that any changes made to the allowed or blocked apps are immediately reflected in the media sessions displayed.
    /// </summary>
    private static void SaveAndRefreshMedia()
    {
        SettingsManager.SaveSettings();
        var mainWindow = Application.Current.MainWindow as MainWindow;

        mainWindow?.RefreshFilteredMedia();
    }

    /// <summary>
    /// Normalizes the specified application name by removing the ".exe" extension and matching it against known session names.
    /// </summary>
    /// <remarks>If the application name matches or is contained within any current media session name, the
    /// method returns the matched session name. This is helpful to compare against known sources and different aliases they may have.</remarks>
    /// <param name="app">The application name to normalize. This may include the ".exe" extension.</param>
    /// <returns>A normalized application name that matches a known media source, else the default or default with extension stripped.</returns>
    private static string NormalizeAppName(string app)
    {
        if (app.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase))
        {
            app = app[..^4];
        }

        var mainWindow = Application.Current.MainWindow as MainWindow;
        if (mainWindow?.mediaManager == null) return app;

        var match = mainWindow.mediaManager.CurrentMediaSessions.Values
            .Select(s => MediaPlayerData.GetAndCacheMediaPlayerData(s.Id).Item1)
            .FirstOrDefault(name => name.Equals(app, System.StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains(app, System.StringComparison.OrdinalIgnoreCase) ||
                                    app.Contains(name, System.StringComparison.OrdinalIgnoreCase));

        return match ?? app;
    }

    /// <summary>
    /// Populates the specified ComboBox with a distinct, alphabetically ordered list of applications that have media.
    /// </summary>
    /// <remarks>If there are no active media sessions, the ComboBox will be populated with an empty list.</remarks>
    /// <param name="comboBox">The ComboBox to populate with the list of applications. (Should not be null).</param>
    private static void PopulateComboBox(ComboBox comboBox)
    {
        var mainWindow = Application.Current.MainWindow as MainWindow;

        if (mainWindow?.mediaManager == null) return;

        var apps = mainWindow.mediaManager.CurrentMediaSessions.Values
            .Select(s => MediaPlayerData.GetAndCacheMediaPlayerData(s.Id).Item1)
            .Distinct()
            .OrderBy(a => a)
            .ToList();

        comboBox.ItemsSource = apps;
    }

    /// <summary>
    /// Handles the DropDownOpened event for the AllowComboBox control and populates its items when the dropdown is opened.
    /// </summary>
    /// <param name="sender">The source of the event (typically AllowComboBox).</param>
    /// <param name="e">An EventArgs object that contains the event data.</param>
    private void AllowComboBox_DropDownOpened(object sender, System.EventArgs e)
    {
        PopulateComboBox(AllowComboBox);
    }

    /// <summary>
    /// Handles the DropDownOpened event for the BlockComboBox control and populates its items when the dropdown is opened.
    /// </summary>
    /// <param name="sender">The source of the event (typically BlockComboBox).</param>
    /// <param name="e">An EventArgs object that contains the event data.</param>
    private void BlockComboBox_DropDownOpened(object sender, System.EventArgs e)
    {
        PopulateComboBox(BlockComboBox);
    }

    /// <summary>
    /// Handles the Click event of the AddAllow button to add the selected application to the allowed applications list.
    /// </summary>
    /// <param name="sender">The source of the event (typically the AddAllow button).</param>
    /// <param name="e">The event data associated with the Click event.</param>
    private void AddAllow_Click(object sender, RoutedEventArgs e)
    {
        var app = AllowComboBox.SelectedItem?.ToString()?.Trim();

        if (string.IsNullOrEmpty(app) || SettingsManager.Current.AllowedApps.Any(a => a.Equals(app, System.StringComparison.OrdinalIgnoreCase))) return;

        SettingsManager.Current.AllowedApps.Add(app);
        AllowComboBox.SelectedIndex = -1;

        SaveAndRefreshMedia();
    }

    /// <summary>
    /// Handles the Click event for adding a manually entered application to the allowed applications list.
    /// </summary>
    /// <remarks>If the entered application name is empty or already exists in the allowed list, no action is
    /// taken. </remarks>
    /// <param name="sender">The source of the event (typically the Add button)</param>
    /// <param name="e">The event data associated with the Click event.</param>
    private void AddAllowManual_Click(object sender, RoutedEventArgs e)
    {
        var app = AllowTextBox.Text?.Trim();

        if (string.IsNullOrEmpty(app)) return;

        app = NormalizeAppName(app);

        if (SettingsManager.Current.AllowedApps.Any(a => a.Equals(app, System.StringComparison.OrdinalIgnoreCase))) return;

        SettingsManager.Current.AllowedApps.Add(app);
        AllowTextBox.Text = string.Empty;

        SaveAndRefreshMedia();
    }

    /// <summary>
    /// Handles the click event to remove an application from the allowed list.
    /// </summary>
    /// <remarks>This method updates the allowed applications list and refreshes the media settings after removal. </remarks>
    /// <param name="sender">The source of the event (usually a Button with its Tag property set to the application identifier). </param>
    /// <param name="e">The event data associated with the click event.</param>
    private void RemoveAllow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string app }) return;

        SettingsManager.Current.AllowedApps.Remove(app);
        SaveAndRefreshMedia();
    }

    /// <summary>
    /// Handles the Click event for adding a selected application to the blocked applications list.
    /// </summary>
    /// <remarks>If the selected application is not already blocked and is not null or empty, it is added to
    /// the blocked applications list. The media is then refreshed.</remarks>
    /// <param name="sender">The source of the event (typically the button that was clicked).</param>
    /// <param name="e">The event data associated with the click event.</param>
    private void AddBlock_Click(object sender, RoutedEventArgs e)
    {
        var app = BlockComboBox.SelectedItem?.ToString()?.Trim();

        if (string.IsNullOrEmpty(app) || SettingsManager.Current.BlockedApps.Any(b => b.Equals(app, System.StringComparison.OrdinalIgnoreCase))) return;

        SettingsManager.Current.BlockedApps.Add(app);
        BlockComboBox.SelectedIndex = -1;

        SaveAndRefreshMedia();
    }

    /// <summary>
    /// Handles the Click event for manually adding an application to the blocked list.
    /// </summary>
    /// <remarks>Adds the trimmed text from the input box to the blocked applications list if it is not empty
    /// and not already present. After adding, the input box is cleared and the media list is refreshed.</remarks>
    /// <param name="sender">The source of the event (typically the button that was clicked).</param>
    /// <param name="e">The event data associated with the click event.</param>
    private void AddBlockManual_Click(object sender, RoutedEventArgs e)
    {
        var app = BlockTextBox.Text?.Trim();

        if (string.IsNullOrEmpty(app)) return;

        app = NormalizeAppName(app);

        if (SettingsManager.Current.BlockedApps.Any(b => b.Equals(app, System.StringComparison.OrdinalIgnoreCase))) return;

        SettingsManager.Current.BlockedApps.Add(app);
        BlockTextBox.Text = string.Empty;

        SaveAndRefreshMedia();
    }

    /// <summary>
    /// Handles the click event to remove an application from the blocked list.
    /// </summary>
    /// <param name="sender">The source of the event, expected to be a Button with its Tag property set to the application.</param>
    /// <param name="e">The event data associated with the click event.</param>
    private void RemoveBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string app }) return;

        SettingsManager.Current.BlockedApps.Remove(app);

        SaveAndRefreshMedia();
    }
}