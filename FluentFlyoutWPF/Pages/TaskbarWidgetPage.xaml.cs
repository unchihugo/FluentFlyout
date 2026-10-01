// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class TaskbarWidgetPage : Page
{
    public TaskbarWidgetPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
        UpdateMonitorList();
        Loaded += (_, _) => LoadPriorityList();
    }

    // hide-order dropdowns for adaptive width; the album icon is always kept as the fallback
    private static readonly string[] PriorityElements =
    [
        WidgetLayoutElementNames.Controls,
        WidgetLayoutElementNames.SongText,
        WidgetLayoutElementNames.Visualizer,
    ];

    private bool _loadingPrioritySelection;
    private int[] _previousPrioritySelection = [0, 1, 2];

    private void LoadPriorityList()
    {
        var order = SettingsManager.Current.TaskbarWidgetPriorityOrder
            .Where(WidgetLayoutElementNames.IsKnown)
            .Where(name => name != WidgetLayoutElementNames.Icon)
            .Distinct()
            .ToList();

        foreach (var name in PriorityElements)
            if (!order.Contains(name))
                order.Add(name);

        _loadingPrioritySelection = true;
        SetPrioritySelection(PriorityHiddenFirstComboBox, order[0]);
        SetPrioritySelection(PriorityHiddenSecondComboBox, order[1]);
        SetPrioritySelection(PriorityHiddenLastComboBox, order[2]);
        _previousPrioritySelection = [PriorityHiddenFirstComboBox.SelectedIndex, PriorityHiddenSecondComboBox.SelectedIndex, PriorityHiddenLastComboBox.SelectedIndex];
        _loadingPrioritySelection = false;
    }

    private static void SetPrioritySelection(ComboBox comboBox, string name)
    {
        int index = Array.IndexOf(PriorityElements, name);
        comboBox.SelectedIndex = Math.Clamp(index, 0, PriorityElements.Length - 1);
    }

    private static string? GetPriorityValue(ComboBox comboBox) =>
        comboBox.SelectedIndex >= 0 ? PriorityElements[comboBox.SelectedIndex] : null;

    private void PriorityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPrioritySelection)
            return;

        string? changed = GetPriorityValue(sender as ComboBox);
        if (changed == null)
            return;

        // resolve duplicate picks by swapping: the other dropdown holding this value
        // takes the value the changed dropdown just released
        ComboBox[] boxes = [PriorityHiddenFirstComboBox, PriorityHiddenSecondComboBox, PriorityHiddenLastComboBox];
        int changedIndex = Array.FindIndex(boxes, box => ReferenceEquals(box, sender));
        for (int i = 0; i < boxes.Length; i++)
        {
            if (i == changedIndex)
                continue;

            int valueIndex = Array.IndexOf(PriorityElements, GetPriorityValue(boxes[i]));
            if (valueIndex >= 0 && PriorityElements[valueIndex] == changed)
            {
                int previous = _previousPrioritySelection[changedIndex];
                _loadingPrioritySelection = true;
                boxes[i].SelectedIndex = Math.Clamp(previous, 0, PriorityElements.Length - 1);
                _loadingPrioritySelection = false;
            }
        }

        _previousPrioritySelection = [PriorityHiddenFirstComboBox.SelectedIndex, PriorityHiddenSecondComboBox.SelectedIndex, PriorityHiddenLastComboBox.SelectedIndex];

        var resolved = boxes.Select(GetPriorityValue).ToList();
        if (resolved.Any(p => p == null) || resolved.Distinct().Count() != 3)
            return;

        SettingsManager.Current.TaskbarWidgetPriorityOrder =
        [
            WidgetLayoutElementNames.Icon,
            .. resolved.Cast<string>(),
        ];
        SettingsManager.SaveSettings();
    }

    private void UpdateMonitorList()
    {
        MonitorUtil.UpdateMonitorList(
            TaskbarWidgetSelectedMonitorComboBox,
            () => SettingsManager.Current.TaskbarWidgetSelectedMonitor,
            value => SettingsManager.Current.TaskbarWidgetSelectedMonitor = value);
    }
}