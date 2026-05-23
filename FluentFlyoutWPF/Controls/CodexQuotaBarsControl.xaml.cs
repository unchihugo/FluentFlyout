// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes.Utils;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FluentFlyout.Controls;

/// <summary>
/// Interaction logic for CodexQuotaBarsControl.xaml
/// </summary>
public partial class CodexQuotaBarsControl : UserControl
{
    private double _sessionQuotaFillRatio;
    private double _weeklyQuotaFillRatio;

    public CodexQuotaBarsControl()
    {
        InitializeComponent();
        SessionQuotaTrackBorder.SizeChanged += (s, e) => UpdateQuotaFillWidth(SessionQuotaTrackBorder, SessionQuotaFillRectangle, _sessionQuotaFillRatio);
        WeeklyQuotaTrackBorder.SizeChanged += (s, e) => UpdateQuotaFillWidth(WeeklyQuotaTrackBorder, WeeklyQuotaFillRectangle, _weeklyQuotaFillRatio);
    }

    public void ApplyRows(CodexUsageQuotaBarRow[] rows)
    {
        SetQuotaRow(
            SessionQuotaBarRow,
            SessionQuotaUsageText,
            SessionQuotaPercentText,
            SessionQuotaResetText,
            SessionQuotaTrackBorder,
            SessionQuotaFillRectangle,
            TryGetQuotaRow(rows, "session", out CodexUsageQuotaBarRow sessionRow),
            sessionRow,
            ref _sessionQuotaFillRatio);

        SetQuotaRow(
            WeeklyQuotaBarRow,
            WeeklyQuotaUsageText,
            WeeklyQuotaPercentText,
            WeeklyQuotaResetText,
            WeeklyQuotaTrackBorder,
            WeeklyQuotaFillRectangle,
            TryGetQuotaRow(rows, "weekly", out CodexUsageQuotaBarRow weeklyRow),
            weeklyRow,
            ref _weeklyQuotaFillRatio);
    }

    public void Clear()
    {
        SessionQuotaBarRow.Visibility = Visibility.Collapsed;
        WeeklyQuotaBarRow.Visibility = Visibility.Collapsed;
        SessionQuotaUsageText.Text = string.Empty;
        SessionQuotaPercentText.Text = string.Empty;
        SessionQuotaResetText.Text = string.Empty;
        WeeklyQuotaUsageText.Text = string.Empty;
        WeeklyQuotaPercentText.Text = string.Empty;
        WeeklyQuotaResetText.Text = string.Empty;
        _sessionQuotaFillRatio = 0d;
        _weeklyQuotaFillRatio = 0d;
        SessionQuotaFillRectangle.Width = 0d;
        WeeklyQuotaFillRectangle.Width = 0d;
    }

    public void ApplyTextStyle(FontFamily fontFamily, double fontSize)
    {
        foreach (TextBlock textBlock in GetQuotaTextBlocks())
        {
            textBlock.FontFamily = fontFamily;
            textBlock.FontSize = fontSize;
        }
    }

    private static bool TryGetQuotaRow(CodexUsageQuotaBarRow[] rows, string label, out CodexUsageQuotaBarRow row)
    {
        foreach (CodexUsageQuotaBarRow candidate in rows)
        {
            if (string.Equals(candidate.Label, label, StringComparison.Ordinal))
            {
                row = candidate;
                return true;
            }
        }

        row = default;
        return false;
    }

    private static void SetQuotaRow(
        FrameworkElement rowElement,
        TextBlock usageText,
        TextBlock percentText,
        TextBlock resetText,
        FrameworkElement trackElement,
        Rectangle fillRectangle,
        bool visible,
        CodexUsageQuotaBarRow row,
        ref double fillRatio)
    {
        rowElement.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (!visible)
        {
            usageText.Text = string.Empty;
            percentText.Text = string.Empty;
            resetText.Text = string.Empty;
            fillRatio = 0d;
            fillRectangle.Width = 0d;
            return;
        }

        usageText.Text = row.UsageText;
        percentText.Text = row.RemainingPercentText;
        resetText.Text = row.ResetText;
        fillRatio = row.FillRatio;
        UpdateQuotaFillWidth(trackElement, fillRectangle, fillRatio);
    }

    private static void UpdateQuotaFillWidth(FrameworkElement trackElement, Rectangle fillRectangle, double fillRatio)
    {
        fillRectangle.Width = Math.Max(0d, trackElement.ActualWidth * Math.Clamp(fillRatio, 0d, 1d));
    }

    private IEnumerable<TextBlock> GetQuotaTextBlocks()
    {
        yield return SessionQuotaLabelText;
        yield return SessionQuotaUsageText;
        yield return SessionQuotaPercentText;
        yield return SessionQuotaResetText;
        yield return WeeklyQuotaLabelText;
        yield return WeeklyQuotaUsageText;
        yield return WeeklyQuotaPercentText;
        yield return WeeklyQuotaResetText;
    }
}
