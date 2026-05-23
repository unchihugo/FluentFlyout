// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes.Utils;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FluentFlyout.Controls;

/// <summary>
/// Interaction logic for TaskbarCodexUsageControl.xaml
/// </summary>
public partial class TaskbarCodexUsageControl : UserControl
{
    private CodexUsageWidgetService? _codexUsageWidgetService;

    public TaskbarCodexUsageControl()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

        Unloaded += (s, e) =>
        {
            AttachCodexUsageService(null);
            ClearCodexUsageDisplay();
        };

        UpdateCodexUsageDisplay();
    }

    public void SetMainWindow(MainWindow mainWindow)
    {
        AttachCodexUsageService(mainWindow.CodexUsageWidgetService);
    }

    internal bool IsClockSideVisible()
    {
        return IsCodexUsageVisible();
    }

    public (double logicalWidth, double logicalHeight) CalculateSize()
    {
        ApplyCodexQuotaStyle();
        UpdateCodexUsageDisplay();

        if (!IsCodexUsageVisible())
            return (0, 0);

        double fontSize = SystemUsageStyleHelper.NormalizeFontSize(SettingsManager.Current.TaskbarWidgetSystemStatsFontSize);
        return (GetCodexUsageViewportWidth(fontSize), 40);
    }

    private static double GetCodexUsageViewportWidth(double fontSize)
    {
        return Math.Clamp(fontSize * 23.0, 300, 340);
    }

    private bool IsCodexUsageVisible()
    {
        return TaskbarWidgetPassiveSlotVisibilityHelper.ShouldShowCodexUsageStandalone(
            widgetEnabled: SettingsManager.Current.TaskbarWidgetEnabled,
            codexUsageEnabled: SettingsManager.Current.TaskbarWidgetCodexUsageEnabled,
            codexUsageHasData: _codexUsageWidgetService?.HasData == true,
            pinCodexUsageToClockSide: SettingsManager.Current.TaskbarWidgetCodexUsageClockSideEnabled);
    }

    private void CodexUsageViewportBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsCodexUsageVisible())
        {
            _codexUsageWidgetService?.Refresh();
            e.Handled = true;
        }
    }

    private void AttachCodexUsageService(CodexUsageWidgetService? codexUsageWidgetService)
    {
        if (ReferenceEquals(_codexUsageWidgetService, codexUsageWidgetService))
        {
            UpdateCodexUsageDisplay();
            return;
        }

        if (_codexUsageWidgetService != null)
            _codexUsageWidgetService.SnapshotChanged -= CodexUsageWidgetService_SnapshotChanged;

        _codexUsageWidgetService = codexUsageWidgetService;

        if (_codexUsageWidgetService != null)
            _codexUsageWidgetService.SnapshotChanged += CodexUsageWidgetService_SnapshotChanged;

        UpdateCodexUsageDisplay();
    }

    private void CodexUsageWidgetService_SnapshotChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => UpdateCodexUsageDisplay());
    }

    private void UpdateCodexUsageVisibility()
    {
        bool visible = IsCodexUsageVisible();
        Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        CodexUsageViewportBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        CodexUsageViewportBorder.Cursor = visible ? Cursors.Hand : Cursors.Arrow;

        if (visible)
        {
            double fontSize = SystemUsageStyleHelper.NormalizeFontSize(SettingsManager.Current.TaskbarWidgetSystemStatsFontSize);
            Width = GetCodexUsageViewportWidth(fontSize);
            CodexUsageViewportBorder.Width = Width;
        }
        else
        {
            ClearCodexUsageDisplay();
        }
    }

    private void UpdateCodexUsageDisplay()
    {
        UpdateCodexUsageVisibility();

        if (!IsCodexUsageVisible())
        {
            ClearCodexUsageDisplay();
            return;
        }

        CodexUsageQuotaBarRow[] rows = CodexUsageTextFormatter.FormatQuotaBars(
            _codexUsageWidgetService?.CurrentSnapshot ?? default,
            DateTime.UtcNow);
        CodexQuotaBars.ApplyRows(rows);
    }

    private void ClearCodexUsageDisplay()
    {
        CodexQuotaBars.Clear();
    }

    private void ApplyCodexQuotaStyle()
    {
        string fontFamily = SystemUsageStyleHelper.NormalizeFontFamily(
            SettingsManager.Current.TaskbarWidgetSystemStatsFontFamily,
            SystemUsageStyleHelper.DefaultFontFamily);

        FontFamily statsFontFamily = new(fontFamily);
        double fontSize = SystemUsageStyleHelper.NormalizeFontSize(SettingsManager.Current.TaskbarWidgetSystemStatsFontSize);

        CodexQuotaBars.ApplyTextStyle(statsFontFamily, Math.Clamp(fontSize - 2d, 9d, 11d));
    }
}
