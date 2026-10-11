// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using GongSolutions.Wpf.DragDrop;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class TaskbarWidgetPage : Page, IDropTarget
{
    public TaskbarWidgetPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
        UpdateMonitorList();
        Loaded += (_, _) => LoadPriorityList();
    }

    // drag-reorder items for the adaptive-width hide order; the album icon is always the
    // fallback and never appears in the list
    private sealed class PriorityItem(string name, string displayName) : INotifyPropertyChanged
    {
        public string Name { get; } = name;
        public string DisplayName { get; } = displayName;

        private string _roleText = string.Empty;

        // localized "Hidden first/second/last" hint, refreshed when the order changes
        public string RoleText
        {
            get => _roleText;
            set
            {
                if (_roleText == value)
                    return;
                _roleText = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private readonly ObservableCollection<PriorityItem> _priorityItems = [];

    private void LoadPriorityList()
    {
        var order = SettingsManager.Current.TaskbarWidgetPriorityOrder
            .Where(WidgetLayoutElementNames.IsKnown)
            .Where(name => name != WidgetLayoutElementNames.Icon)
            .Distinct()
            .ToList();

        foreach (var name in WidgetLayoutElementNames.All)
            if (name != WidgetLayoutElementNames.Icon && !order.Contains(name))
                order.Add(name);

        _priorityItems.Clear();
        foreach (var name in order)
        {
            var resource = name switch
            {
                WidgetLayoutElementNames.Controls => "TaskbarWidgetPriorityControls",
                WidgetLayoutElementNames.SongText => "TaskbarWidgetPrioritySongText",
                WidgetLayoutElementNames.Visualizer => "TaskbarWidgetPriorityVisualizer",
                _ => null,
            };
            var displayName = resource != null
                ? (TryFindResource(resource) as string ?? name)
                : name;
            _priorityItems.Add(new PriorityItem(name, displayName));
        }

        RefreshRoleTexts();
        PriorityListBox.ItemsSource = _priorityItems;
    }

    private void RefreshRoleTexts()
    {
        string[] keys =
        [
            "TaskbarWidgetPriorityHiddenFirst",
            "TaskbarWidgetPriorityHiddenSecond",
            "TaskbarWidgetPriorityHiddenLast",
        ];

        for (int i = 0; i < _priorityItems.Count; i++)
        {
            var key = i < keys.Length ? keys[i] : keys[^1];
            _priorityItems[i].RoleText = TryFindResource(key) as string ?? key;
        }
    }

    // GongSolutions drop handler: the list reorders itself, we persist the new order
    void IDropTarget.DragOver(IDropInfo dropInfo)
    {
        dropInfo.Effects = DragDropEffects.Move;
    }

    void IDropTarget.Drop(IDropInfo dropInfo)
    {
        var source = dropInfo.Data as PriorityItem;
        if (source == null)
            return;

        int insertIndex = dropInfo.InsertIndex;
        int oldIndex = _priorityItems.IndexOf(source);
        if (oldIndex < 0)
            return;

        _priorityItems.Move(oldIndex, Math.Clamp(insertIndex, 0, _priorityItems.Count - 1));
        RefreshRoleTexts();

        // lowest entry hides first (matches the solver's hide ladder)
        SettingsManager.Current.TaskbarWidgetPriorityOrder =
        [
            WidgetLayoutElementNames.Icon,
            .. _priorityItems.Select(item => item.Name),
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